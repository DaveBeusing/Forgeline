namespace ForgeLine.Graphics;

// Device-owned scratch only. Context lifetime, ownership and disposal are checked before lookup.
internal sealed class TextureBindingCache
{
    private readonly object?[] _slots = new object[16];
    internal bool Matches(int slot, object texture) => (uint)slot < (uint)_slots.Length && ReferenceEquals(_slots[slot], texture);
    internal void Remember(int slot, object texture) => _slots[slot] = texture;
    internal void Reset() => Array.Clear(_slots);
}

internal sealed partial class D3D12GraphicsDevice
{
    private readonly TextureBindingCache _textureBindings = new();
    internal void InvalidateTextureBindings() => _textureBindings.Reset();
    internal bool TextureBindingMatches(int slot, D3D12GraphicsTexture texture) => _textureBindings.Matches(slot, texture);
    internal void RememberTextureBinding(int slot, D3D12GraphicsTexture texture) => _textureBindings.Remember(slot, texture);
}
