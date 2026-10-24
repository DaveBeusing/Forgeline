using System.Security.Cryptography;
using System.Text.Json;

namespace ForgeLine.Game;

public static class ScenarioContentIdentity
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { IncludeFields = true };

    public const string DirectorateKey = "directorate.v1";
    public const string StartRule = "directorate.starting-base.v1";
    public const string ObjectiveRule = "command-core-elimination.v1";

    // Catalogs expose definitions in stable ID order. Include fields for value IDs and vectors.
    public static string ComputeSha256(MatchContent content) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            Key = DirectorateKey, StartRule, ObjectiveRule,
            Resources = content.Resources.Definitions.ToArray(),
            Buildings = content.Buildings.Definitions.ToArray(),
            Units = content.Units.Definitions.ToArray(),
            Technologies = content.Technologies.Definitions.ToArray(),
            Recipes = content.Recipes.Definitions.ToArray(),
            Weapons = content.Weapons.Definitions.ToArray(),
            Armor = content.Armor.Definitions.ToArray(),
            Artillery = content.Artillery.Definitions.ToArray()
        }, SerializerOptions)));
}
