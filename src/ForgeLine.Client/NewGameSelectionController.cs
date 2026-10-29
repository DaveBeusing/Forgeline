using System.Buffers.Binary;
using System.Security.Cryptography;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal interface ISeedClipboard
{
    bool Copy(string value);
    bool Paste(out string value);
}
internal sealed class WindowsSeedClipboard(nint owner) : ISeedClipboard
{
    public bool Copy(string value) => WindowsClipboard.TryWrite(owner, value);
    public bool Paste(out string value) => WindowsClipboard.TryRead(owner, out value);
}
internal sealed class PreparedGeneratedMap(GeneratedMapPackage package) : IDisposable
{
    internal GeneratedMapPackage Package { get; } = package;
    public void Dispose() { } // Immutable managed data; the coordinator still owns transfer/cancellation.
}

/// <summary>Window-owner input and copied progress; one existing construction coordinator owns preparation.</summary>
internal sealed class NewGameSelectionController : IDisposable
{
    private readonly NewGameModel _model;
    private readonly ClientGeneratedMapStore _maps;
    private readonly ISeedClipboard _clipboard;
    private readonly Func<GeneratedMapRecipe, Action, CancellationToken, GeneratedMapPackage>? _prepare;
    private ClientSessionLoadingCoordinator<PreparedGeneratedMap>? _worker;
    private GeneratedMapPackage? _prepared;
    private GeneratedMapRecipe? _recipe;
    internal bool SelectAll { get => _model.SeedSelectAll; private set => _model.SeedSelectAll = value; }
    internal bool ConsumedBack { get; private set; }
    internal bool Busy => _worker is not null;
    internal bool PreparationFinished => _worker?.Completed == true;

    internal NewGameSelectionController(NewGameModel model, ClientGeneratedMapStore maps, ISeedClipboard clipboard,
        Func<GeneratedMapRecipe, Action, CancellationToken, GeneratedMapPackage>? prepare = null)
    {
        _model = model; _maps = maps; _clipboard = clipboard; _prepare = prepare;
        if (model.Generated.Ready) model.Generated.Invalidate();
    }
    internal void Poll()
    {
        if (_worker is null) return;
        if (_worker.Progress.Phase == ClientSessionLoadPhase.HashVerification) _model.Generated.Verifying();
        if (!_worker.Completed) return;
        if (_worker.CancellationRequested)
        {
            _model.Generated.Invalidate();
            _model.Generated.Notify(SeededSkirmishText.Get("Cancelled"));
        }
        else if (_worker.Failure is { } failure)
        {
            Console.Error.WriteLine("[map:preparation-failed] " + failure);
            bool qualification = failure.InnerException?.Message.StartsWith("Generated map qualification failed", StringComparison.Ordinal) == true;
            _model.Generated.Fail(SeededSkirmishText.Get(qualification ? "QualificationError" : "PreparationError"));
        }
        else if (_worker.TryTake(out var result))
        {
            using (result)
            {
                _prepared = result!.Package;
                var manifest = _prepared.Manifest;
                if (manifest.Recipe != _recipe)
                {
                    _prepared = null;
                    _model.Generated.Fail(SeededSkirmishText.Get("PreparationError"));
                }
                else _model.Generated.Complete(new(manifest.Recipe.GeneratorVersion, manifest.GameplaySha256, manifest.VisualSha256));
            }
        }
        _worker.Dispose(); _worker = null;
    }

    internal ClientSessionRequest? Handle(InputState input, FrontendLayout layout, bool enter, bool escape, bool pointerPressed)
    {
        bool wasPreparing = Busy;
        Poll();
        _model.VisibleCapacity = layout.Geometry.VisibleDetailRows;
        ConsumedBack = false;
        // Publish readiness/hash before a new input action may start a session.
        if (wasPreparing && !Busy) return null;
        enter |= input.WasKeyPressed(PlatformKey.Enter);
        escape |= input.WasKeyPressed(PlatformKey.Escape);
        ConsumedBack = false;
        _model.VisibleCapacity = layout.Geometry.VisibleDetailRows;
        bool generated = _model.MapKind == SkirmishMapKind.Generated;
        bool secondary = pointerPressed && input.HasPointerPosition && FrontendHitTesting.SecondaryAction(input.PointerPosition.X, input.PointerPosition.Y, layout);
        if (Busy)
        {
            if (escape || secondary) { _worker!.Cancel(); _model.Generated.Cancel(); ConsumedBack = true; }
            return null;
        }
        if (input.FocusLostThisFrame) return null;
        bool tab = input.WasKeyPressed(PlatformKey.Tab);
        if (tab) { _model.MoveFocus(input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift) ? -1 : 1); SelectAll = true; }
        if (generated)
        {
            if (input.WasKeyPressed(PlatformKey.Up) || input.WheelDelta > 0) { _model.MoveFocus(-1); SelectAll = true; }
            if (input.WasKeyPressed(PlatformKey.Down) || input.WheelDelta < 0) { _model.MoveFocus(1); SelectAll = true; }
        }
        else
        {
            if (input.WasKeyPressed(PlatformKey.Up)) _model.MoveScenario(-1);
            if (input.WasKeyPressed(PlatformKey.Down)) _model.MoveScenario(1);
        }
        int direction = input.WasKeyPressed(PlatformKey.Left) ? -1 : input.WasKeyPressed(PlatformKey.Right) ? 1 : 0;
        if (direction != 0) Adjust(direction);
        if (pointerPressed && input.HasPointerPosition &&
            FrontendHitTesting.DetailRow(input.PointerPosition.X, input.PointerPosition.Y, layout, _model.RowCount) is { } row)
        {
            _model.FocusRow(row + _model.VisibleStart); SelectAll = true;
            int adjust = layout.Geometry.Decrease(row).Contains(input.PointerPosition.X, input.PointerPosition.Y) ? -1 : 1;
            if (_model.FocusedRow == _model.MapKindRow || generated && _model.FocusedRow is 2 or 3) Adjust(adjust);
            else if (!generated && _model.FocusedRow == 0) _model.MoveScenario(adjust);
            else if (!generated && _model.FocusedRow == _model.SeedRow &&
                (layout.Geometry.Decrease(row).Contains(input.PointerPosition.X, input.PointerPosition.Y) ||
                 layout.Geometry.Increase(row).Contains(input.PointerPosition.X, input.PointerPosition.Y))) Adjust(adjust);
            else if (generated && _model.FocusedRow is >= 5 and <= 7) Action(_model.FocusedRow);
        }
        generated = _model.MapKind == SkirmishMapKind.Generated;
        if (generated)
        {
            if (input.WasKeyPressedWithControl(PlatformKey.A)) { _model.FocusRow(1); SelectAll = true; }
            if (input.WasKeyPressedWithControl(PlatformKey.C)) Action(6);
            if (input.WasKeyPressedWithControl(PlatformKey.V)) { _model.FocusRow(1); Action(7); }
            if (_model.FocusedRow == 1)
            {
                if (input.WasKeyPressed(PlatformKey.Delete)) _model.Generated.SetSeedText(string.Empty);
                foreach (char character in input.TextInput)
                {
                    if (character == '\b')
                    {
                        string text = _model.Generated.SeedText;
                        _model.Generated.SetSeedText(SelectAll || text.Length == 0 ? string.Empty : text[..^1]); SelectAll = false;
                    }
                    else if (!char.IsControl(character))
                    {
                        _model.Generated.SetSeedText((SelectAll ? string.Empty : _model.Generated.SeedText) + character); SelectAll = false;
                    }
                }
            }
        }
        bool primary = pointerPressed && input.HasPointerPosition && FrontendHitTesting.PrimaryAction(input.PointerPosition.X, input.PointerPosition.Y, layout);
        if (enter && _model.FocusedRow == _model.MapKindRow) { Adjust(1); return null; }
        if (enter && generated && _model.FocusedRow is >= 5 and <= 7) { Action(_model.FocusedRow); return null; }
        return enter || primary ? Primary() : null;
    }

    private void Adjust(int direction)
    {
        if (_model.FocusedRow == _model.MapKindRow)
        {
            _model.SelectMapKind(_model.MapKind == SkirmishMapKind.Authored ? SkirmishMapKind.Generated : SkirmishMapKind.Authored);
            SelectAll = true;
        }
        else if (_model.MapKind == SkirmishMapKind.Authored)
        {
            ulong seed = _model.Configuration.Seed;
            _model.SetSeed(direction < 0 ? seed == 0 ? 0 : seed - 1 : seed == ulong.MaxValue ? seed : seed + 1);
        }
        else if (_model.FocusedRow == 2) _model.Generated.AdjustSize(direction);
        else if (_model.FocusedRow == 3) _model.Generated.AdjustProfile(direction);
    }
    private void Action(int row)
    {
        if (row == 5)
        {
            ulong seed = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8));
            _model.Generated.SetSeedText(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (row == 6) _model.Generated.Notify(SeededSkirmishText.Get(_clipboard.Copy(_model.Generated.SeedText) ? "Copied" : "ClipboardError"));
        if (row == 7)
        {
            if (_clipboard.Paste(out string value)) _model.Generated.SetSeedText(value.Trim());
            else { _model.Generated.SetSeedText(string.Empty); _model.Generated.Notify(SeededSkirmishText.Get("ClipboardError")); }
            SelectAll = true;
        }
    }
    internal ClientSessionRequest? Primary()
    {
        if (Busy) return null;
        if (_model.MapKind == SkirmishMapKind.Authored)
            return _model.CanStartSelectedScenario ? ClientSessionRequest.NewGame(_model.Configuration.Seed, _model.SelectedScenario.Key) : null;
        if (!_model.Generated.TryCreateRecipe(out var recipe)) { _model.Generated.Notify(SeededSkirmishText.Get("SeedError")); return null; }
        if (_model.Generated.Ready && _prepared is not null && _recipe == recipe)
            return ClientSessionRequest.NewGame(recipe!.Terrain.Generation.Seed, _prepared.CompositionKey) with
            { Identity = _prepared.CreateComposition().Identity, GeneratedManifestSha256 = _prepared.ManifestSha256 };
        _recipe = recipe; _prepared = null;
        _model.Generated.Begin();
        _worker = new((token, report) =>
        {
            report(new(ClientSessionLoadPhase.ScenarioAssembly));
            var package = (_prepare ?? _maps.Prepare)(recipe!, () => report(new(ClientSessionLoadPhase.HashVerification)), token);
            token.ThrowIfCancellationRequested();
            return new PreparedGeneratedMap(package);
        });
        return null;
    }
    public void Dispose() { _worker?.Dispose(); _worker = null; _prepared = null; }
}
