using System.Globalization;
using System.Resources;
using ForgeLine.Game;

namespace ForgeLine.UI;

public enum SkirmishMapKind { Authored, Generated }
public enum SeededSkirmishState { Draft, Generating, Verifying, Cancelling, Ready, Failed }
public enum SeededSkirmishProfile { Temperate, Sparse }
public readonly record struct SeededSkirmishIdentity(int GeneratorVersion, string GameplaySha256, string VisualSha256);

public static class SeededSkirmishText
{
    private static readonly ResourceManager Resources = new("ForgeLine.UI.SeededSkirmishText", typeof(SeededSkirmishText).Assembly);
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}

/// <summary>Frontend draft and copied qualification identity. No runtime or generation ownership.</summary>
public sealed class SeededSkirmishModel
{
    public const int MaximumSeedLength = 22;
    private static readonly int[] Sizes = [8, 12, 16];
    private bool _rejectedSeed;
    public string SeedText { get; private set; } = "17";
    public int SizeInChunks { get; private set; } = 12;
    public SeededSkirmishProfile Profile { get; private set; }
    public int Players { get; } = 2;
    public SeededSkirmishState State { get; private set; }
    public SeededSkirmishIdentity? Identity { get; private set; }
    public string Feedback { get; private set; } = string.Empty;
    public bool Busy => State is SeededSkirmishState.Generating or SeededSkirmishState.Verifying or SeededSkirmishState.Cancelling;
    public bool Ready => State == SeededSkirmishState.Ready && Identity.HasValue;

    public static bool TryParseSeed(string? text, out ulong seed)
    {
        seed = 0;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumSeedLength) return false;
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        ReadOnlySpan<char> digits = hex ? text.AsSpan(2) : text.AsSpan();
        if (digits.IsEmpty || hex && digits.Length > 16 || !hex && digits.Length > 20) return false;
        foreach (char digit in digits)
            if (!(digit is >= '0' and <= '9' || hex && digit is >= 'a' and <= 'f' or >= 'A' and <= 'F')) return false;
        return ulong.TryParse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out seed);
    }

    public void SetSeedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Busy) return;
        if (text.Length > MaximumSeedLength) { Invalidate(); _rejectedSeed = true; Feedback = SeededSkirmishText.Get("SeedError"); return; }
        _rejectedSeed = false;
        SeedText = text;
        Invalidate();
        if (!TryParseSeed(text, out _)) Feedback = SeededSkirmishText.Get("SeedError");
    }
    public void AdjustSize(int direction)
    {
        if (Busy || direction == 0) return;
        SizeInChunks = Sizes[Math.Clamp(Array.IndexOf(Sizes, SizeInChunks) + Math.Sign(direction), 0, Sizes.Length - 1)];
        Invalidate();
    }
    public void AdjustProfile(int direction)
    {
        if (Busy || direction == 0) return;
        Profile = Profile == SeededSkirmishProfile.Temperate ? SeededSkirmishProfile.Sparse : SeededSkirmishProfile.Temperate;
        Invalidate();
    }
    public bool TryCreateRecipe(out GeneratedMapRecipe? recipe)
    {
        recipe = null;
        if (_rejectedSeed || !TryParseSeed(SeedText, out ulong seed)) return false;
        var defaults = new GeneratedMapRecipe();
        recipe = defaults with
        {
            Terrain = defaults.Terrain with { Generation = defaults.Terrain.Generation with
            { Seed = seed, WidthInChunks = SizeInChunks, HeightInChunks = SizeInChunks } },
            ScatterDensityPermille = Profile == SeededSkirmishProfile.Sparse ? 350 : 650
        };
        recipe.Validate();
        return true;
    }
    public void Begin() { State = SeededSkirmishState.Generating; Identity = null; Feedback = string.Empty; }
    public void Verifying() { if (Busy && State != SeededSkirmishState.Cancelling) State = SeededSkirmishState.Verifying; }
    public void Cancel() { if (Busy) State = SeededSkirmishState.Cancelling; }
    public void Complete(SeededSkirmishIdentity identity) { Identity = identity; State = SeededSkirmishState.Ready; Feedback = string.Empty; }
    public void Fail(string message) { Identity = null; State = SeededSkirmishState.Failed; Feedback = message; }
    public void Invalidate() { State = SeededSkirmishState.Draft; Identity = null; Feedback = string.Empty; }
    public void Notify(string message) => Feedback = message;
}
