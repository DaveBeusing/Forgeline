using System.Numerics;

namespace ForgeLine.Presentation;

public enum TacticalTeamPalette : byte { Standard, BlueOrange, Grayscale }

/// <summary>Presentation preferences only; never part of a simulation snapshot or command.</summary>
public readonly record struct TacticalDisplaySettings
{
    public const int CurrentVersion = 1;
    public int Version { get; init; }
    public bool HighContrast { get; init; }
    public TacticalTeamPalette TeamPalette { get; init; }

    public TacticalDisplaySettings() => Version = CurrentVersion;

    public void Validate()
    {
        if (Version != CurrentVersion || !Enum.IsDefined(TeamPalette))
            throw new InvalidDataException("Unsupported tactical display preferences.");
    }

    internal Vector4 TeamColor(bool enemy, Vector4 standard) => TeamPalette switch
    {
        TacticalTeamPalette.BlueOrange => enemy ? new(1f, .4f, .02f, standard.W) : new(.18f, .72f, 1f, standard.W),
        TacticalTeamPalette.Grayscale => enemy ? new(.55f, .55f, .55f, standard.W) : new(.95f, .95f, .95f, standard.W),
        _ => standard
    };
}
