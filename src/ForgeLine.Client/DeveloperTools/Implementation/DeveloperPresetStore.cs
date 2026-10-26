#if FORGELINE_DEVELOPER_TOOLS
using ForgeLine.Game;

namespace ForgeLine.Client;

internal sealed class DeveloperPresetStore
{
    private static readonly object s_gate = new();
    internal const int MaximumVisiblePresets = 64;
    private readonly string _root;
    private readonly Func<string, MatchComposition> _resolveComposition;

    internal DeveloperPresetStore(Func<string, MatchComposition>? resolveComposition = null, string? root = null)
    {
        string basePath = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FORGELINE", "Developer", "Presets");
        if (string.IsNullOrWhiteSpace(basePath)) throw new InvalidOperationException("Local developer profile directory is unavailable.");
        _root = Path.GetFullPath(basePath);
        _resolveComposition = resolveComposition ?? ScenarioRegistry.Default.Resolve;
    }

    internal string Root => _root;
    internal string? ImportFailure { get; private set; }

    internal IReadOnlyList<DeveloperPresetDocument> List()
    {
        lock (s_gate)
        {
            EnsureRoot();
            var files = Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly)
                .Take(MaximumVisiblePresets + 1).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length > MaximumVisiblePresets) throw new IOException("Developer preset store exceeds its 64-file limit.");
            var documents = new List<DeveloperPresetDocument>(files.Length);
            foreach (string file in files)
            {
                EnsureRegularFile(file);
                byte[] bytes = ReadBounded(file);
                try { documents.Add(DeveloperPresetCodec.Deserialize(bytes, _resolveComposition)); }
                catch (DeveloperPresetException) { /* Invalid files remain untouched and are not selectable. */ }
            }
            return Array.AsReadOnly(documents.OrderBy(document => document.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(document => document.Key, StringComparer.Ordinal).ToArray());
        }
    }

    internal DeveloperPresetDocument Load(string key)
    {
        lock (s_gate)
        {
            string path = ResolveKey(key);
            EnsureRoot(); EnsureRegularFile(path);
            return DeveloperPresetCodec.Deserialize(ReadBounded(path), _resolveComposition);
        }
    }

    internal IReadOnlyList<DeveloperPresetDocument> ListIncoming()
    {
        lock (s_gate)
        {
            string root = EnsureImportRoot();
            ImportFailure = null;
            var files = Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumVisiblePresets + 1).ToArray();
            if (files.Length > MaximumVisiblePresets) throw new IOException("Preset import directory exceeds its 64-file limit.");
            var documents = new List<DeveloperPresetDocument>();
            foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                if (!DeveloperPresetCodec.IsPresetKey(key)) throw new IOException("Import filenames must be stable preset keys.");
                EnsureRegularFile(file);
                DeveloperPresetDocument document;
                try { document = DeveloperPresetCodec.Deserialize(ReadBounded(file), _resolveComposition); }
                catch (Exception error) when (error is DeveloperPresetException or IOException)
                { ImportFailure = "An incoming preset is invalid or incompatible; existing presets are unchanged."; continue; }
                if (document.Key != key) throw new IOException("Import filename must match its preset key.");
                documents.Add(document);
            }
            return documents.AsReadOnly();
        }
    }

    internal DeveloperPresetDocument ImportIncoming(string key)
    {
        if (!DeveloperPresetCodec.IsPresetKey(key)) throw new ArgumentException("Invalid import key.", nameof(key));
        lock (s_gate)
        {
            string path = Path.Combine(EnsureImportRoot(), key + ".json");
            EnsureRegularFile(path);
            return Import(ReadBounded(path));
        }
    }

    private string EnsureImportRoot()
    {
        EnsureRoot();
        string directory = Path.Combine(_root, "Import");
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Preset import directory cannot be a link.");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Preset import directory cannot be a link.");
        return directory;
    }

    internal DeveloperPresetDocument Import(ReadOnlySpan<byte> utf8)
    {
        // Validate the complete document before touching the managed store.
        DeveloperPresetDocument document = DeveloperPresetCodec.Deserialize(utf8, _resolveComposition);
        byte[] canonical = DeveloperPresetCodec.Serialize(document, _resolveComposition);
        lock (s_gate)
        {
            EnsureRoot();
            if (Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumVisiblePresets + 1).Count() >= MaximumVisiblePresets)
                throw new IOException("Developer preset store is full.");
            string path = ResolveKey(document.Key);
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A preset with this key already exists.");
            WriteNewAtomic(path, canonical);
        }
        return document;
    }

    internal void Export(DeveloperPresetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        // Complete validation and size checks happen before the prior file can be replaced.
        byte[] canonical = DeveloperPresetCodec.Serialize(document, _resolveComposition);
        lock (s_gate)
        {
            EnsureRoot();
            string path = ResolveKey(document.Key);
            if (!File.Exists(path) && Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumVisiblePresets).Count() >= MaximumVisiblePresets)
                throw new IOException("Developer preset store is full.");
            if (File.Exists(path))
            {
                EnsureRegularFile(path);
                WriteReplaceAtomic(path, canonical);
            }
            else WriteNewAtomic(path, canonical);
        }
    }

    private string ResolveKey(string key)
    {
        if (!DeveloperPresetCodec.IsPresetKey(key)) throw new ArgumentException("Preset key is invalid.", nameof(key));
        string path = Path.GetFullPath(Path.Combine(_root, key + ".json"));
        if (!string.Equals(Path.GetDirectoryName(path), _root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Preset key escapes the managed store.", nameof(key));
        return path;
    }

    private void EnsureRoot()
    {
        // Check existing ancestors before CreateDirectory can follow a redirected parent.
        var ancestor = new DirectoryInfo(_root);
        while (ancestor is not null)
        {
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Developer preset store cannot be inside a linked directory.");
            ancestor = ancestor.Parent;
        }
        Directory.CreateDirectory(_root);
        var attributes = File.GetAttributes(_root);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Developer preset store cannot use a link or non-directory root.");
    }

    private static void EnsureRegularFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Preset file was not found.", path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Developer preset files cannot be links or reparse points.");
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > DeveloperPresetCodec.MaximumDocumentBytes) throw new IOException("Preset file exceeds the 64 KiB limit.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Preset changed while it was being read.");
        return bytes;
    }

    private void WriteNewAtomic(string destination, byte[] bytes)
    {
        string temporary = Path.Combine(_root, ".preset-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, destination);
        }
        finally { TryDeleteTemporary(temporary); }
    }

    private void WriteReplaceAtomic(string destination, byte[] bytes)
    {
        string temporary = Path.Combine(_root, ".preset-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            EnsureRegularFile(destination);
            File.Replace(temporary, destination, null, ignoreMetadataErrors: true);
        }
        finally { TryDeleteTemporary(temporary); }
    }

    private static void TryDeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
#endif
