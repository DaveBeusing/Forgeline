#!/usr/bin/env python3
"""Independently check WAV bytes, hashes, headroom, loop cuts and current UI limits."""
from __future__ import annotations
import argparse, hashlib, json, struct, sys, wave
from pathlib import Path
import numpy as np
from scipy.signal import resample_poly

def validate(root: Path, output: Path) -> dict:
    manifest=json.loads((root/'assets/source/audio/library.json').read_text())
    expected={'navigation':6720,'intent':10560,'accepted':18240,'rejected':21120}
    rows=[];failures=[];seen=set();hashes=set()
    for item in manifest['assets']:
        path=root/item['path'];raw=path.read_bytes()
        with wave.open(str(path),'rb') as w:
            ch,width,rate,count=w.getnchannels(),w.getsampwidth(),w.getframerate(),w.getnframes()
            pcm=w.readframes(count)
        x=np.frombuffer(pcm,dtype='<i2').reshape(-1,ch).astype(np.float64)/32768
        truepeak=float(np.max(np.abs(resample_poly(x,4,1,axis=0))))
        mean=float(np.max(np.abs(x.mean(axis=0))))
        steps=np.abs(np.diff(x,axis=0))
        seam=float(np.max(np.abs(x[0]-x[-1])))
        p99=float(np.quantile(steps,.99))
        digest=hashlib.sha256(raw).hexdigest()
        checks={
            'riff_size':raw[:4]==b'RIFF' and struct.unpack_from('<I',raw,4)[0]==len(raw)-8,
            'canonical_wave_header':raw[8:16]==b'WAVEfmt ' and struct.unpack_from('<I',raw,16)[0]==16 and raw[36:40]==b'data',
            'pcm_48k_16bit':width==2 and rate==48000 and struct.unpack_from('<H',raw,20)[0]==1,
            'no_extra_chunks':len(raw)==len(pcm)+44,
            'metadata_matches':ch==item['channels'] and count==item['frames'] and len(pcm)==item['pcm_bytes'],
            'sha256_matches':digest==item['sha256'],
            'no_clipping':np.max(np.abs(x))<.999,
            'true_peak_below_minus_3_dbfs':truepeak<10**(-3/20),
            'dc_below_minus_60_dbfs':mean<.001,
            'unique_id':item['id'] not in seen,
            'unique_audio':digest not in hashes,
            'one_shot_silent_endpoints':bool(item['loop']) or bool(np.all(x[0]==0) and np.all(x[-1]==0)),
            'loop_cut_not_an_outlier':not item['loop'] or seam<=max(p99,.0001),
        }
        if item['bus']=='ui':
            n=path.stem
            checks['existing_ui_exact_byte_budget']=n in expected and len(pcm)==expected[n] and len(raw)==44+expected[n] and ch==2
        checks={key:bool(value) for key,value in checks.items()}
        seen.add(item['id']);hashes.add(digest)
        bad=[k for k,v in checks.items() if not v]
        if bad: failures.append({'file':item['path'],'checks':bad})
        rows.append({'id':item['id'],'checks':checks,'true_peak_dbfs':round(20*np.log10(max(truepeak,1e-12)),3),
                     'dc_offset':mean,'loop_seam_jump':seam,'within_clip_step_p99':p99})
    report={'assets':len(rows),'passed_assets':len(rows)-len(failures),'failures':failures,
            'loop_count':sum(a['loop'] for a in manifest['assets']),
            'total_pcm_bytes':sum(a['pcm_bytes'] for a in manifest['assets']),
            'total_seconds':round(sum(a['duration_seconds'] for a in manifest['assets']),3),
            'methods':['Independent RIFF/PCM parser','SHA-256','4x polyphase true-peak estimate','DC/clip check','Loop cut versus internal 99th-percentile sample steps','Exact current UI byte-size contract'],
            'not_executed':['C# UiPcmClip.Read execution','.NET build/tests','Windows waveOut playback','Subjective listening review','Physical multivoice mix and device-loss qualification'],
            'results':rows}
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report,indent=2)+'\n')
    return report

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[2]);p.add_argument('--output',type=Path,default=Path('artifacts/audio-signal-validation.json'));args=p.parse_args()
    report=validate(args.root,args.output);print(json.dumps({k:v for k,v in report.items() if k!='results'},indent=2))
    sys.exit(bool(report['failures']))
if __name__=='__main__':main()
