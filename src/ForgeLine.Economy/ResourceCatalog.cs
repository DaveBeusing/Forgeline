using System.Diagnostics.CodeAnalysis;
using ForgeLine.Core;

namespace ForgeLine.Economy;

public sealed class ResourceCatalog
{
    private readonly Dictionary<ResourceId, ResourceDefinition> _definitions;
    private readonly Dictionary<string, ResourceId> _idsByKey;

    public ResourceCatalog(IEnumerable<ResourceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _definitions = new Dictionary<ResourceId, ResourceDefinition>();
        _idsByKey = new Dictionary<string, ResourceId>(StringComparer.Ordinal);

        foreach (ResourceDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();

            if (!_definitions.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Duplicate resource ID '{definition.Id}'.",
                    nameof(definitions));
            }

            if (!_idsByKey.TryAdd(definition.Key, definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate resource key '{definition.Key}'.",
                    nameof(definitions));
            }
        }
    }

    public int Count => _definitions.Count;

    public IEnumerable<ResourceDefinition> Definitions =>
        _definitions.OrderBy(static pair => pair.Key).Select(static pair => pair.Value);

    public ResourceDefinition this[ResourceId id] =>
        _definitions.TryGetValue(id, out ResourceDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown resource ID '{id}'.");

    public bool TryGet(
        ResourceId id,
        [NotNullWhen(true)] out ResourceDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    public bool TryResolve(
        string key,
        out ResourceId id)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _idsByKey.TryGetValue(key, out id);
    }
}
