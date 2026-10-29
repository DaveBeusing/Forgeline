using System.Globalization;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class SeededSkirmishTests
{
    private static readonly string Assets = new('a', 64);
    private static readonly Lazy<GeneratedMapPackage> Package = new(() => GeneratedMapCompiler.Compile(new(), Assets));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("0", 0UL)]
    [InlineData("17", 17UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData("0xffffffffffffffff", ulong.MaxValue)]
    [InlineData("0XAbCd", 43981UL)]
    public void DecimalAndExplicitHexSeedsAreExact(string text, ulong expected)
    { Assert.True(SeededSkirmishModel.TryParseSeed(text, out ulong value)); Assert.Equal(expected, value); }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 17")]
    [InlineData("17 ")]
    [InlineData("0x")]
    [InlineData("FFFF")]
    [InlineData("18446744073709551616")]
    [InlineData("0x10000000000000000")]
    [InlineData("1e3")]
    public void MalformedOrOverflowingSeedsCannotPrepareOrStart(string text)
    {
        var draft = new SeededSkirmishModel(); draft.SetSeedText(text);
        Assert.False(draft.TryCreateRecipe(out _)); Assert.False(draft.Ready); Assert.NotEmpty(draft.Feedback);
    }

    [Fact]
    public void SeedSizeAndProfileEditsInvalidateQualificationAndRespectCompilerBounds()
    {
        var draft = new SeededSkirmishModel();
        draft.Complete(new(1, new string('a', 64), new string('b', 64)));
        draft.SetSeedText(new string('1', 100));
        Assert.False(draft.Ready); Assert.False(draft.TryCreateRecipe(out _));
        draft.SetSeedText("0x11");
        draft.AdjustSize(-1); draft.AdjustSize(-1);
        Assert.Equal(8, draft.SizeInChunks);
        draft.AdjustSize(1); draft.AdjustSize(1); draft.AdjustSize(1);
        Assert.Equal(16, draft.SizeInChunks);
        draft.AdjustProfile(1);
        Assert.True(draft.TryCreateRecipe(out var recipe));
        recipe!.Validate(); Assert.Equal(350, recipe.ScatterDensityPermille); Assert.Equal(17UL, recipe.Terrain.Generation.Seed);
        Assert.Equal(2, draft.Players);
    }

    [Fact]
    public void TextPreservesOrderRepeatAndFocusSuppression()
    {
        var input = new InputState();
        foreach (char c in "0xff11") input.Apply(PlatformInputEvent.Text(c));
        Assert.Equal("0xff11", new string(input.TextInput));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.D1));
        input.SuppressHeldInput(); input.Apply(PlatformInputEvent.Text('1')); Assert.True(input.TextInput.IsEmpty);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.D1));
        input.Apply(PlatformInputEvent.Text('2')); input.Apply(PlatformInputEvent.FocusLost());
        Assert.True(input.TextInput.IsEmpty);
        for (int i = 0; i < 1000; i++) input.Apply(PlatformInputEvent.Text('3'));
        Assert.Equal(64, input.TextInput.Length); input.BeginFrame(); Assert.True(input.TextInput.IsEmpty);
    }

    [Fact]
    public void AuthoredPracticeSelectionAndSeedSurviveSwitchingModes()
    {
        var model = new NewGameModel([new("authored", "Authored", true), new("practice", "Practice", true)]);
        Assert.Equal(SkirmishMapKind.Authored, model.MapKind);
        model.SetSeed(123); model.MoveScenario(1); model.SelectMapKind(SkirmishMapKind.Generated);
        model.Generated.SetSeedText("999"); model.SelectMapKind(SkirmishMapKind.Authored);
        Assert.Equal("practice", model.SelectedScenario.Key); Assert.Equal(123UL, model.Configuration.Seed);
        Assert.True(model.CanStartSelectedScenario);
    }

    [Theory]
    [InlineData(1280, 720, 1f, 96f)]
    [InlineData(1280, 720, 2f, 192f)]
    [InlineData(3440, 1440, 2f, 144f)]
    public void PointerFocusScrollingAndFullHashUseRenderedGeometry(int width, int height, float scale, float dpi)
    {
        using var directory = new TemporaryMaps(); var model = new NewGameModel();
        using var controller = new NewGameSelectionController(model, directory.Store, new Clipboard());
        var layout = FrontendDesign.ResolveLayout(width, height, scale, dpi);
        model.VisibleCapacity = layout.Geometry.VisibleDetailRows; model.FocusRow(model.MapKindRow);
        var input = new InputState(); var mode = layout.Geometry.DetailRow(model.MapKindRow - model.VisibleStart);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, (int)(mode.X + 20), (int)(mode.Y + 10)));
        Assert.Null(controller.Handle(input, layout, false, false, true)); Assert.Equal(SkirmishMapKind.Generated, model.MapKind);
        model.Generated.Complete(new(1, new string('a', 64), new string('b', 64)));
        for (int row = 10; row < 18; row++)
        {
            model.FocusRow(row); var view = FrontendPresentationAdapter.NewGame(model);
            Assert.Equal(18, view.TotalDetailRows); Assert.InRange(view.DetailLines.Count, 1, layout.Geometry.VisibleDetailRows);
            var focused = Assert.Single(view.DetailLines, line => line.IsFocused);
            Assert.Equal(new string(row < 14 ? 'a' : 'b', 16), focused.Value);
            Assert.True(layout.Geometry.DetailRow(row - model.VisibleStart).Interactive);
        }
    }

    [Fact]
    public void ClipboardTypingBackspaceAndRandomizeAreActualActions()
    {
        using var directory = new TemporaryMaps(); var model = new NewGameModel(); model.SelectMapKind(SkirmishMapKind.Generated);
        var clipboard = new Clipboard { Value = "0x11" };
        using var controller = new NewGameSelectionController(model, directory.Store, clipboard);
        var layout = FrontendDesign.ResolveLayout(1920, 1080);
        var input = new InputState(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftControl));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.V));
        controller.Handle(input, layout, false, false, false); Assert.Equal("0x11", model.Generated.SeedText);
        model.FocusRow(6); Assert.Null(controller.Handle(new(), layout, true, false, false)); Assert.Equal("0x11", clipboard.Value);
        model.FocusRow(1); input = new(); input.Apply(PlatformInputEvent.Text('9'));
        controller.Handle(input, layout, false, false, false); Assert.Equal("9", model.Generated.SeedText);
        input = new(); input.Apply(PlatformInputEvent.Text('\b'));
        controller.Handle(input, layout, false, false, false); Assert.Equal(string.Empty, model.Generated.SeedText);
        model.FocusRow(5); controller.Handle(new(), layout, true, false, false);
        Assert.True(model.Generated.TryCreateRecipe(out _)); Assert.False(model.Generated.Ready);
    }

    [Fact]
    public async Task PreparationCannotAutoStartAndNormalSessionSaveRestartRetainVerifiedMap()
    {
        using var directory = new TemporaryMaps(); var model = new NewGameModel(); model.SelectMapKind(SkirmishMapKind.Generated);
        using var controller = new NewGameSelectionController(model, directory.Store, new Clipboard());
        Assert.Null(controller.Primary()); Assert.True(controller.Busy); Assert.False(model.CanStartSelectedScenario);
        await Complete(controller);
        Assert.True(model.Generated.Ready); var request = controller.Primary(); Assert.NotNull(request);
        using var session = ClientSessionFactory.CreateOwned(request.Value, _ => { }, Token, generatedMaps: directory.Store);
        Assert.Equal(model.Generated.Identity!.Value.GameplaySha256, session.Runtime.RuntimeSettings.Composition.Identity!.MapSha256);
        Assert.NotNull(session.TerrainProfile); Assert.NotNull(session.Decorations); Assert.NotNull(session.GeneratedMap);
        Assert.Equal(session.GeneratedMap.Manifest.DecorationInstances, session.Decorations.Capture().InstanceCount);
        session.Runtime.Simulation.RunTicks(2, Token);
        string savePath = System.IO.Path.Combine(directory.Path, "test.save.json");
        var save = MatchPersistenceService.CaptureSave(session.Runtime);
        MatchPersistenceSerializer.WriteSaveCancellable(savePath, save, Token);
        using var restored = ClientSessionFactory.CreateOwned(ClientSessionRequest.Load(new("test", "test", savePath, 2, LoadGameEntryState.Available)), _ => { }, Token, generatedMaps: directory.Store);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored.Runtime).ComputeSha256());
        using var restarted = ClientSessionFactory.CreateOwned(ClientSessionRequest.Restart(restored.Runtime.RuntimeSettings), _ => { }, Token, generatedMaps: directory.Store);
        Assert.Equal(save.Configuration.Identity, restarted.Runtime.RuntimeSettings.Composition.Identity);
        Assert.Equal(0UL, restarted.Runtime.Simulation.CurrentTick.Value);
        var wrongAssets = new ClientGeneratedMapStore(directory.Path, new string('b', 64));
        Assert.Throws<ClientGeneratedMapException>(() => ClientSessionFactory.CreateOwned(request.Value, _ => { }, Token, generatedMaps: wrongAssets));
        File.Delete(System.IO.Path.Combine(directory.Path, request.Value.GeneratedManifestSha256!, GeneratedMapPackage.TerrainFileName));
        var missing = Assert.Throws<ClientGeneratedMapException>(() => ClientSessionFactory.CreateOwned(request.Value, _ => { }, Token, generatedMaps: directory.Store));
        Assert.Equal(ClientSessionLoadError.GeneratedMap, ClientSessionLoadingException.From(missing, ClientSessionLoadPhase.Configuration).Category);
        model.Generated.SetSeedText("18"); Assert.False(model.Generated.Ready); Assert.Null(controller.Primary());
    }

    [Fact]
    public async Task CancellationPumpsWithoutTransferringCandidateAndFailureAllowsRetry()
    {
        using var directory = new TemporaryMaps(); var model = new NewGameModel(); model.SelectMapKind(SkirmishMapKind.Generated);
        using var entered = new ManualResetEventSlim();
        using var controller = new NewGameSelectionController(model, directory.Store, new Clipboard(), (_, _, token) =>
        { entered.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return Package.Value; });
        Assert.Null(controller.Primary()); Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Token));
        controller.Handle(new(), FrontendDesign.ResolveLayout(1920,1080), false, true, false);
        Assert.True(controller.ConsumedBack); Assert.Equal(SeededSkirmishState.Cancelling, model.Generated.State);
        await Complete(controller); Assert.False(model.Generated.Ready); Assert.Equal(SeededSkirmishState.Draft, model.Generated.State);
        using var failed = new NewGameSelectionController(model, directory.Store, new Clipboard(), (_, _, _) => throw new InvalidDataException("Generated map qualification failed: resource opening."));
        Assert.Null(failed.Primary()); await Complete(failed);
        Assert.Equal(SeededSkirmishState.Failed, model.Generated.State); Assert.Contains("SEED", model.Generated.Feedback);
        model.Generated.SetSeedText("17"); Assert.Equal(SeededSkirmishState.Draft, model.Generated.State);
    }

    [Fact]
    public void KeyboardSourceSelectionDoesNotStartAnAuthoredMatch()
    {
        using var directory = new TemporaryMaps(); var model = new NewGameModel();
        using var controller = new NewGameSelectionController(model, directory.Store, new Clipboard());
        var input = new InputState(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Tab));
        var layout = FrontendDesign.ResolveLayout(1920,1080);
        Assert.Null(controller.Handle(input,layout,false,false,false)); Assert.Equal(model.MapKindRow,model.FocusedRow);
        Assert.Null(controller.Handle(new(),layout,true,false,false)); Assert.Equal(SkirmishMapKind.Generated,model.MapKind);
        Assert.False(controller.Busy); Assert.False(model.CanStartSelectedScenario);
    }

    [Fact]
    public async Task CandidateMustMatchTheRequestedSeedAndPreset()
    {
        using var directory = new TemporaryMaps(); var model = new NewGameModel(); model.SelectMapKind(SkirmishMapKind.Generated);
        model.Generated.SetSeedText("18");
        using var controller = new NewGameSelectionController(model,directory.Store,new Clipboard(),(_,_,_) => Package.Value);
        Assert.Null(controller.Primary()); await Complete(controller);
        Assert.False(model.Generated.Ready); Assert.Equal(SeededSkirmishState.Failed,model.Generated.State);
    }

    [Fact]
    public void NewTextResourcesResolveInEnglishAndGerman()
    {
        var before = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE"); Assert.Equal("KARTE VALIDIEREN", SeededSkirmishText.Get("Validate"));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US"); Assert.Equal("VALIDATE MAP", SeededSkirmishText.Get("Validate")); }
        finally { CultureInfo.CurrentUICulture = before; }
    }
    private static async Task Complete(NewGameSelectionController controller)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        while (controller.Busy) { controller.Poll(); await Task.Delay(10, timeout.Token); }
    }
    private sealed class Clipboard : ISeedClipboard
    {
        internal string Value { get; set; } = "17";
        public bool Copy(string value) { Value = value; return true; }
        public bool Paste(out string value) { value = Value; return true; }
    }
    private sealed class TemporaryMaps : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgeline-selection-" + Guid.NewGuid().ToString("N"));
        internal ClientGeneratedMapStore Store => new(Path, Assets);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive:true); }
    }
}
