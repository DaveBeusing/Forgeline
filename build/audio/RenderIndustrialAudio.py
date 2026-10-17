#!/usr/bin/env python3
"""Render the FORGELINE industrial sound library from deterministic DSP recipes.
Offline authoring only; never call this generator from a gameplay/audio callback.
Requires Python 3.11+, NumPy and SciPy. No downloaded sound recordings are used.
"""
from __future__ import annotations
import argparse, hashlib, json, math, struct, wave
from pathlib import Path
import numpy as np
from scipy import signal

RATE = 48_000
REVISION = '4641a3114b57421a04ce9da449e9a95b41168d6d'

def rng_for(key: str) -> np.random.Generator:
    return np.random.default_rng(int.from_bytes(hashlib.sha256(key.encode()).digest()[:8], 'little'))

def timebase(seconds: float) -> np.ndarray:
    return np.arange(round(seconds * RATE), dtype=np.float64) / RATE

def noise(n: int, rng: np.random.Generator, low: float = 30, high: float = 10_000) -> np.ndarray:
    x = rng.standard_normal(n)
    sos = signal.butter(3, [low, high], btype='bandpass', fs=RATE, output='sos')
    x = signal.sosfilt(sos, x)
    return x / (np.std(x) + 1e-12)

def decay(t: np.ndarray, tau: float, attack: float = .001) -> np.ndarray:
    return (1 - np.exp(-t / attack)) * np.exp(-t / tau)

def sweep(t: np.ndarray, f0: float, f1: float, tau: float) -> np.ndarray:
    return np.sin(2 * np.pi * (f1*t + (f0-f1)*tau*(1-np.exp(-t/tau))))

def place(dst: np.ndarray, src: np.ndarray, seconds: float, gain: float = 1.0) -> None:
    start = round(seconds * RATE)
    if start >= len(dst): return
    count = min(len(src), len(dst)-start)
    dst[start:start+count] += gain * src[:count]

def echo(x: np.ndarray, taps: tuple[tuple[float,float],...]) -> np.ndarray:
    out = x.copy()
    for delay,gain in taps: place(out, x, delay, gain)
    return out

def metal(t: np.ndarray, rng: np.random.Generator, base: float = 330, tau: float = .08) -> np.ndarray:
    ratios = [1, 1.43, 2.37, 3.19, 4.51]
    out = np.zeros_like(t)
    for j,r in enumerate(ratios):
        out += (.55**j) * np.sin(2*np.pi*base*r*t + rng.uniform(-.2,.2)) * decay(t,tau/(1+j*.28))
    return out

def ui(kind: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t=timebase(seconds)
    freq={'navigation':900,'intent':610,'accepted':760,'rejected':230}[kind]
    x=.15*noise(len(t),rng,1200,9000)*decay(t,.004)
    if kind=='accepted':
        x += .5*np.sin(2*np.pi*760*t)*decay(t,.020)
        place(x, .46*np.sin(2*np.pi*1140*t)*decay(t,.019), .037)
    elif kind=='rejected':
        x += .55*sweep(t,330,180,.035)*decay(t,.031)
        place(x,.22*metal(t,rng,250,.025),.037)
    else:
        x += .42*metal(t,rng,freq,.010 if kind=='navigation' else .017)
        x += .23*sweep(t,180,90,.008)*decay(t,.012)
    return x

def shot(kind: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t=timebase(seconds); n=len(t)
    heavy={'rifle':0,'machine_gun':0,'tank':1,'artillery':2,'rocket':1}.get(kind,0)
    variant=rng.uniform(.93,1.07)
    if kind=='rocket':
        env=(1-np.exp(-t/.015))*np.exp(-t/.27)
        return .65*noise(n,rng,80,5000)*env + .5*sweep(t,190,48,.16)*decay(t,.35)
    x=.30*noise(n,rng,1800,11000)*decay(t,.006+heavy*.005)
    x+=.65*noise(n,rng,65,2800 if heavy else 6500)*decay(t,.025+heavy*.045)
    x+=.9*sweep(t,(155 if heavy else 270)*variant,(43 if heavy else 110)*variant,.020+heavy*.018)*decay(t,.045+heavy*.055)
    place(x,.22*metal(t,rng,690 if not heavy else 190,.035+heavy*.018),.025+heavy*.02)
    x+=.13*noise(n,rng,70,1500)*decay(t,.08+heavy*.17,.008)
    return echo(x,((.045+heavy*.035,.17),(.090+heavy*.08,.09),(.15+heavy*.13,.05)))

def explosion(kind: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t=timebase(seconds); n=len(t)
    scale={'small':.8,'vehicle':1.25,'building':1.75}[kind]
    x=1.2*sweep(t,115,29,.07)*decay(t,.21*scale,.002)
    x+=.75*noise(n,rng,32,2200)*decay(t,.17*scale,.003)
    x+=.25*noise(n,rng,2000,9500)*decay(t,.030,.001)
    x+=.26*noise(n,rng,30,330)*decay(t,.50*scale,.010)
    for when in rng.uniform(.10,seconds*.7,8):
        shard=.12*metal(t,rng,rng.uniform(180,1600),rng.uniform(.015,.09))
        place(x,shard,float(when),float(np.exp(-when/.65)))
    return echo(x,((.13,.16),(.32,.10),(.57,.05)))

def impact(kind: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t=timebase(seconds);n=len(t)
    if kind=='metal': return .6*metal(t,rng,415,.16)+.4*noise(n,rng,700,10500)*decay(t,.016)
    if kind=='dirt': return .5*noise(n,rng,65,1700)*decay(t,.064)+.4*sweep(t,175,63,.015)*decay(t,.042)
    return .45*noise(n,rng,100,4900)*decay(t,.035)+.3*metal(t,rng,790,.055)+.3*sweep(t,190,58,.02)*decay(t,.05)

def periodic_noise(n: int, rng: np.random.Generator, low: float, high: float) -> np.ndarray:
    f=np.fft.rfftfreq(n,1/RATE)
    spectrum=np.fft.rfft(rng.standard_normal(n))
    shape=1/(1+(f/high)**6) * (1-np.exp(-(f/low)**4))
    spectrum*=shape
    spectrum[0]=0
    x=np.fft.irfft(spectrum,n)
    return x/(np.std(x)+1e-12)

def loop(kind: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t=timebase(seconds);n=len(t)
    def harmonic(hz: float, phase: float=0):
        hz=round(hz*seconds)/seconds
        return np.sin(2*np.pi*hz*t+phase)
    if kind in ('scout_idle','scout_move','truck_idle','truck_move','tank_idle','tank_move'):
        rpm={'scout_idle':37,'scout_move':68,'truck_idle':25,'truck_move':46,'tank_idle':29,'tank_move':54}[kind]
        x=sum((.7/(j**1.15))*harmonic(rpm*j,.3*j) for j in range(1,13))
        x=np.tanh(x*1.3)*.60
        x+=.12*periodic_noise(n,rng,80,1800)*(1+.26*harmonic(rpm*2))
        if kind.endswith('move'): x+=.16*periodic_noise(n,rng,100,3600)*(1+.2*harmonic(6))
        if kind.startswith('tank'): x+=.15*periodic_noise(n,rng,450,3000)*np.maximum(0,harmonic(9))**8
        return x
    f={'extractor':44,'smelter':61,'refinery':47,'factory':73,'power_plant':50,'depot':38,
       'wind':.25,'industrial':42,'battlefield':31}.get(kind,50)
    if kind=='wind':
        return periodic_noise(n,rng,35,1300)*(.60+.18*harmonic(.125)+.12*harmonic(.375))
    if kind=='battlefield':
        return .65*periodic_noise(n,rng,28,800)*(1+.15*harmonic(.25))+.08*harmonic(34)
    x=.30*harmonic(f)+.16*harmonic(f*2)+.09*harmonic(f*3.1)
    x+=.19*periodic_noise(n,rng,35,1900)*(1+.2*harmonic(.5))
    if kind in ('extractor','factory','depot','industrial'):
        beats={'extractor':3,'factory':1.5,'depot':.5,'industrial':2}[kind]
        gate=np.maximum(0,harmonic(beats))**14
        x+=gate*(.29*periodic_noise(n,rng,160,3500)+.24*harmonic(430))
    if kind=='smelter': x+=.32*periodic_noise(n,rng,80,4400)
    if kind=='refinery': x+=.17*periodic_noise(n,rng,1500,6800)*(1+.3*harmonic(1))
    return x

def mechanism(kind: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t=timebase(seconds);n=len(t)
    if kind=='engine_start':
        x=.22*noise(n,rng,65,1800)*(1-np.exp(-t/.008))*np.exp(-t/.65)
        x+=.50*sweep(t,24,65,.55)*(1-np.exp(-t/.08))*np.exp(-t/.9)
        x+=.14*metal(t,rng,410,.16)
        return x
    if kind=='engine_stop':
        return .5*sweep(t,65,15,.35)*decay(t,.42,.01)+.10*noise(n,rng,80,1000)*decay(t,.35)
    if kind=='incoming_artillery':
        env=np.sin(np.pi*np.minimum(t/seconds,1))**1.4
        return .35*sweep(t,1100,260,.8)*env+.28*noise(n,rng,400,4000)*env
    if kind in ('refuel','repair'):
        env=np.sin(np.pi*t/seconds)**.7
        x=.25*noise(n,rng,150 if kind=='refuel' else 1700,4500)*env
        x+=.16*np.sin(2*np.pi*(90 if kind=='refuel' else 165)*t)*env
        return x
    x=.42*metal(t,rng,200 if 'load' in kind else 530,.070)
    x+=.32*sweep(t,180,55,.02)*decay(t,.060)
    for when in [.12,.28,.43]:
        place(x,.23*metal(t,rng,rng.uniform(180,580),.040),when)
    x+=.12*noise(n,rng,150,2500)*decay(t,.14,.009)
    return x

def notification(kind: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t=timebase(seconds);x=np.zeros_like(t)
    melodies={
        'supply_low':([440,330],[0,.28]), 'supply_critical':([415,311,415,311],[0,.24,.49,.73]),
        'power_low':([280,210],[0,.26]), 'power_restored':([330,495,660],[0,.14,.29]),
        'base_under_attack':([740,555,740],[0,.24,.49]),
        'research_complete':([440,550,660],[0,.17,.34]), 'unit_ready':([440,660],[0,.13]),
        'invalid_order':([260,195],[0,.12]), 'supply_complete':([370,555],[0,.15]),
        'complete':([330,495,660],[0,.10,.22]),
        'victory':([220,330,440,550,660],[0,.26,.52,.8,1.10]),
        'defeat':([330,293,220,165],[0,.35,.7,1.05]),
    }
    notes,starts=melodies[kind]
    for freq,start in zip(notes,starts):
        tau=.23 if kind in ('victory','defeat') else .07
        tone=(np.sin(2*np.pi*freq*t)+.24*np.sin(2*np.pi*freq*2*t)+.08*np.sin(2*np.pi*freq*3*t))*decay(t,tau,.007)
        place(x,tone,start)
    x+=.045*noise(len(t),rng,1400,6000)*decay(t,.017)
    return x

def finish(x: np.ndarray, peak_db: float, is_loop: bool, stereo: bool, rng: np.random.Generator) -> np.ndarray:
    x=np.asarray(x,dtype=np.float64)
    x-=x.mean()
    if is_loop:
        # Circular synthesis has no filter-state discontinuity; choose a quiet cut.
        jumps=np.abs(x-np.roll(x,1))
        slope=np.abs(np.roll(x,-1)-2*x+np.roll(x,1))
        idx=int(np.argmin(jumps+.04*slope+.0001*np.abs(x)))
        x=np.roll(x,-idx)
    else:
        a=min(round(.001*RATE),len(x)//4);r=min(round(.035*RATE),len(x)//4)
        x[:a]*=np.sin(np.linspace(0,np.pi/2,a))**2
        x[-r:]*=np.cos(np.linspace(0,np.pi/2,r))**2
        # Remove residual DC without compromising silent boundaries.
        w=np.sin(np.linspace(0,np.pi,len(x)))**2
        x-=x.sum()/w.sum()*w
        x[0]=x[-1]=0
    peak=np.max(np.abs(x))
    if not np.isfinite(x).all() or peak<=0: raise ValueError('Invalid audio recipe')
    x*=10**(peak_db/20)/peak
    if stereo:
        # Centered UI and alerts; ambience has subtle circular stereo decorrelation.
        if is_loop:
            right=.86*x+.14*np.roll(x,round(.027*RATE))
            x=np.column_stack((x,right))
        else: x=np.column_stack((x,x))
    else: x=x[:,None]
    # Low-level triangular dither; silence is kept exact for one-shot boundaries.
    d=(rng.random(x.shape)-rng.random(x.shape))/32768
    y=np.rint(np.clip(x+d,-.999969,.999969)*32767).astype('<i2')
    if not is_loop: y[0,:]=y[-1,:]=0
    return y

def write_wav(path: Path, pcm: np.ndarray) -> None:
    path.parent.mkdir(parents=True,exist_ok=True)
    with wave.open(str(path),'wb') as out:
        out.setnchannels(pcm.shape[1]);out.setsampwidth(2);out.setframerate(RATE);out.writeframes(pcm.tobytes())

def build(root: Path) -> list[dict]:
    source=root/'assets/source/audio'; entries=[]
    def add(folder,name,duration,factory,peak=-12,looped=False,stereo=False,bus='world',event=None,variant_group=None):
        rel=Path(folder)/(name+'.wav');key=rel.as_posix();rng=rng_for(key)
        x=factory(duration,rng)
        pcm=finish(x,peak,looped,stereo,rng);path=source/rel;write_wav(path,pcm)
        raw=path.read_bytes(); norm=pcm.astype(np.float64)/32768
        item={'id':'audio.'+'.'.join(rel.with_suffix('').parts),'path':(Path('assets/source/audio')/rel).as_posix(),
              'event':event or name,'bus':bus,'sample_rate':RATE,'channels':pcm.shape[1], 'bits_per_sample':16,
              'frames':len(pcm),'duration_seconds':len(pcm)/RATE,'pcm_bytes':pcm.nbytes,'file_bytes':len(raw),
              'loop':looped,'loop_start_frame':0 if looped else None,'loop_end_frame_exclusive':len(pcm) if looped else None,
              'gain_db':0,'peak_dbfs':round(20*np.log10(np.max(np.abs(norm))),3),
              'rms_dbfs':round(20*np.log10(np.sqrt(np.mean(norm**2))),3),
              'dc_offset':float(np.max(np.abs(np.mean(norm,axis=0)))),
              'clipped_samples':int(np.count_nonzero(np.abs(pcm.astype(np.int32))>=32767)),
              'boundary_jump':float(np.max(np.abs(norm[0]-norm[-1]))),
              'sha256':hashlib.sha256(raw).hexdigest(),
              'variant_group':variant_group,'runtime_status':'existing-ui-format' if folder=='ui' else 'requires-world-audio-integration'}
        entries.append(item)
    for k,d,p in [('navigation',.035,-18),('intent',.055,-15),('accepted',.095,-12),('rejected',.110,-12)]:
        add('ui',k,d,lambda s,r,k=k:ui(k,s,r),p,stereo=True,bus='ui',event='UiAudioCue.'+k.title())
    for kind,d in [('rifle',.42),('machine_gun',.72),('tank',1.5),('artillery',2.2)]:
        for v in range(1,4):
            def make(s,r,kind=kind):
                if kind!='machine_gun':return shot(kind,s,r)
                out=np.zeros(round(s*RATE))
                for at in [0,.085,.17]: place(out,shot('rifle',.35,r),at,.85)
                return out
            add('combat',f'{kind}_fire_{v:02}',d,make,-9,event=kind+'_fire',variant_group='audio.combat.'+kind+'_fire')
    for k,d in [('small',1.25),('vehicle',2.1),('building',3.3)]:
        add('combat','explosion_'+k,d,lambda s,r,k=k:explosion(k,s,r),-10)
    for k in ['dirt','metal','concrete']:
        add('combat','impact_'+k,.5,lambda s,r,k=k:impact(k,s,r),-14)
    for k in ['scout_idle','scout_move','truck_idle','truck_move','tank_idle','tank_move']:
        add('vehicles',k+'_loop',4,lambda s,r,k=k:loop(k,s,r),-18,True,event=k)
    for k,d in [('engine_start',1.6),('engine_stop',1.3)]:
        add('vehicles',k,d,lambda s,r,k=k:mechanism(k,s,r),-15)
    for k in ['extractor','smelter','refinery','factory','power_plant','depot']:
        add('industry',k+'_loop',6,lambda s,r,k=k:loop(k,s,r),-22,True,event=k+'_active')
    for k,d in [('cargo_load',.85),('cargo_unload',.95),('refuel',2),('rearm',1),('repair',1.6),('supply_complete',.5)]:
        add('logistics',k,d,lambda s,r,k=k:notification(k,s,r) if k=='supply_complete' else mechanism(k,s,r),-16)
    for k,d in [('place',.25),('start',1.1),('complete',.6)]:
        add('construction',k,d,lambda s,r,k=k:notification(k,s,r) if k=='complete' else mechanism(k,s,r),-15)
    for k,d in [('supply_low',.8),('supply_critical',1.25),('power_low',.75),('power_restored',.7),('base_under_attack',1.1),('research_complete',.85),('unit_ready',.5),('invalid_order',.4)]:
        add('alerts',k,d,lambda s,r,k=k:notification(k,s,r),-15 if k not in ('base_under_attack','supply_critical') else -12,stereo=True,bus='alerts')
    for k in ['wind','industrial','battlefield']:
        add('ambience',k+'_loop',16,lambda s,r,k=k:loop(k,s,r),-24,True,True,bus='ambience',event=k+'_ambience')
    add('combat','rocket_launch',1.3,lambda s,r:shot('rocket',s,r),-11)
    add('combat','incoming_artillery',1.4,lambda s,r:mechanism('incoming_artillery',s,r),-16)
    for k in ['victory','defeat']:
        add('events',k,2.7,lambda s,r,k=k:notification(k,s,r),-15,stereo=True,bus='alerts')
    manifest={'schema_version':1,'pack_name':'FORGELINE Industrial Sound Library','design_revision':'1.0.0',
              'reviewed_master':REVISION,'runtime_manifest_supported_by_master':False,
              'authoring':'Original deterministic DSP; no external sound samples.',
              'notes':'UI files match current exact lengths. Other clips are authoring assets, not yet wired gameplay sounds. Loop endpoints are metadata, not embedded smpl chunks.',
              'assets':entries}
    (root/'assets/source/audio/library.json').write_text(json.dumps(manifest,indent=2)+'\n')
    return entries

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=Path(__file__).resolve().parents[2])
    args=parser.parse_args();entries=build(args.output)
    print(f'Rendered {len(entries)} WAV assets; {sum(e["file_bytes"] for e in entries):,} bytes')
if __name__=='__main__':main()
