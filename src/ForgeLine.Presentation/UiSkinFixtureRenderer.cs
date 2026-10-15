using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

/// <summary>Native qualification fixture; shares the production skin/text paths and device.</summary>
public sealed class UiSkinFixtureRenderer : IDisposable
{
    private static readonly UiSkinState[] States = Enum.GetValues<UiSkinState>();
    private static readonly string[] StateLabels = States.Select(s => s.ToString()).ToArray();
    private readonly UiSkinSprite[][] _samples = new UiSkinSprite[2][];
    private readonly UiSkinResources[] _resources = new UiSkinResources[3];
    private readonly UiSkinBatch[] _batches = new UiSkinBatch[3];
    private readonly UiTextBatch _labels, _modal;
    public UiSkinDiagnostics CoreDiagnostics => _batches[0].Diagnostics;
    public UiSkinDiagnostics IconDiagnostics => _batches[1].Diagnostics;
    public UiSkinDiagnostics PortraitDiagnostics => _batches[2].Diagnostics;
    public UiTextDiagnostics TextDiagnostics => _labels.Diagnostics;
    public UiSkinFixtureRenderer(IGraphicsDevice device, RuntimeAssetCatalog catalog)
    {
        try
        {
            for (int i=0;i<3;i++) { _resources[i]=new(device,catalog,(UiSkinGroup)i); _batches[i]=new(device,_resources[i]); }
            for (int i=1;i<3;i++) _samples[i-1]=_resources[i].Data.Sprites.Where(s=>_resources[i].Data.Pages[s.Page].Group==(UiSkinGroup)i && s.Width==(i==1?32:48)).Take(i==1?18:8).ToArray();
            _labels=new(device); _modal=new(device);
            if (!_labels.Ready || !_modal.Ready) throw new InvalidDataException("Required fixture font unavailable.");
        }
        catch
        { foreach(var b in _batches)b?.Dispose();foreach(var r in _resources)r?.Dispose();_labels?.Dispose();_modal?.Dispose();throw; }
    }
    public void Render(IGraphicsCommandContext c)
    {
        var core=_batches[0];
        foreach(var b in _batches)b.Begin(c.Width,c.Height,c.FrameIndex);
        _labels.Begin();_modal.Begin();
        for(int i=0;i<States.Length;i++)
        {
            var rect=new UiSkinRect(30+(i%5)*240,35+(i/5)*90,218,64);
            core.Draw("ui.skin.directorate.control.button",rect,Vector4.One,true);
            core.Overlay(States[i],rect,Vector4.One);
            _labels.Add(StateLabels[i],rect.X+20,rect.Y+18,20,Vector4.One,c.Width,c.Height);
        }
        core.SetClip(new(30,230,1180,150));
        core.Draw("ui.skin.directorate.panel.primary",new(10,210,1240,180),Vector4.One,true);
        for(int i=0;i<5;i++)
        {
            var rect=new UiSkinRect(55+i*230,270,200,20);
            core.Draw("ui.skin.directorate.progress.progress_track",rect,Vector4.One,true);
            core.Draw("ui.skin.directorate.progress.progress_fill",rect,Vector4.One,false,i*.25f);
        }
        core.SetClip(new(0,0,c.Width,c.Height));
        core.Tile("ui.skin.directorate.control.row_compact",new(30,410,500,83),Vector4.One);
        core.Draw("ui.skin.directorate.panel.primary",new(555,410,4,4),Vector4.One,true);
        core.Flush(c);
        _labels.Add("0%       25%       50%       75%       100%",65,320,20,Vector4.One,c.Width,c.Height);
        _labels.Render(c);
        // A later translucent panel follows text; batching must preserve this interleaving.
        core.Draw("ui.skin.directorate.panel.dialog",new(740,420,460,125),new(1,1,1,.85f),true);
        core.Flush(c);
        _modal.Add("Live text after modal",770,465,24,Vector4.One,c.Width,c.Height);
        _modal.Render(c);
        var icon=_batches[1];int count=0;
        foreach(var s in _samples[0])
        {
            icon.Draw(s.Id,new(35+count*66,570,48,48),Vector4.One);count++;
        }
        icon.Flush(c);
        var portrait=_batches[2];count=0;
        foreach(var s in _samples[1])
        {
            portrait.Draw(s.Id,new(35+count*75,645,48,48),Vector4.One);count++;
        }
        portrait.Flush(c);
    }
    public void Dispose()
    { _labels.Dispose();_modal.Dispose();foreach(var b in _batches)b.Dispose();foreach(var r in _resources)r.Dispose(); }
}
