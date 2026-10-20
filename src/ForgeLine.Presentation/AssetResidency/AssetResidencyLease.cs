using System.Runtime.CompilerServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class AssetResidencyLease : IDisposable
{
    // Weak device registry only. Entries own no resources after their last explicit lease.
    private static readonly ConditionalWeakTable<IGraphicsDevice, DeviceScope> Scopes = new();
    private readonly DeviceScope _scope;
    private readonly (RuntimeAssetCatalog Catalog, TextureDetailQuality Quality, string Signature) _key;
    private readonly Entry _entry;
    private bool _disposed;
    private AssetResidencyLease(DeviceScope scope, (RuntimeAssetCatalog, TextureDetailQuality, string) key, Entry entry)
    { _scope = scope; _key = key; _entry = entry; }
    internal ResidentAssetStore Store { get { CheckOwner(); return _entry.Store; } }
    internal AssetResidencyProgress Progress
    {
        get
        {
            var store = Store; var cpu = store.Prepared;
            return new(_entry.Generation, cpu.Successful ? store.State : AssetResidencyState.Failed,
                cpu.AssetCount, cpu.ContainerReads, cpu.PayloadBytes, cpu.PreparationMilliseconds, store.PublicationMilliseconds,
                store.MaterialDiagnostics.ResidentMeshBytes, store.ResidentTextureMipBytes, _entry.Owners);
        }
    }
    internal static AssetResidencyLease Acquire(IGraphicsDevice graphics, RuntimeAssetCatalog catalog, TextureDetailQuality quality)
    {
        ArgumentNullException.ThrowIfNull(graphics); ArgumentNullException.ThrowIfNull(catalog);
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        var scope = Scopes.GetValue(graphics, static _ => new());
        if (scope.Owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Residency belongs to the render owner.");
        var key = (catalog, quality, catalog.Manifest.ContentSignature);
        if (!scope.Entries.TryGetValue(key, out var entry))
        {
            // One current profile plus one rebuilding profile; never evict a pinned generation.
            if (scope.Entries.Count >= 2) throw new InvalidOperationException("Dispose an old residency generation before admitting another profile.");
            entry = new(new ResidentAssetStore(graphics, catalog, quality), ++scope.Generation);
            scope.Entries.Add(key, entry);
        }
        entry.Owners++;
        return new(scope, key, entry);
    }
    private void CheckOwner()
    {
        if (_scope.Owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Residency belongs to the render owner.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        _disposed = true;
        if (--_entry.Owners == 0) { _scope.Entries.Remove(_key); _entry.Store.Dispose(); }
    }
    private sealed class DeviceScope
    {
        internal readonly int Owner = Environment.CurrentManagedThreadId;
        internal long Generation;
        internal readonly Dictionary<(RuntimeAssetCatalog, TextureDetailQuality, string), Entry> Entries = [];
    }
    private sealed class Entry(ResidentAssetStore store, long generation)
    {
        internal readonly ResidentAssetStore Store = store;
        internal readonly long Generation = generation;
        internal int Owners;
    }
}
