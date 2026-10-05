using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class RtsUiAssetAuthoringTests
{
    private static readonly string[] RequiredIds =
    [
        "ui.icon.resource.ferrous_ore",
        "ui.icon.resource.volatiles",
        "ui.icon.resource.silicates",
        "ui.icon.resource.rare_elements",
        "ui.icon.resource.steel",
        "ui.icon.resource.fuel",
        "ui.icon.resource.electronics",
        "ui.icon.resource.ammunition",
        "ui.icon.unit_role.infantry",
        "ui.icon.unit_role.reconnaissance",
        "ui.icon.unit_role.armor",
        "ui.icon.unit_role.artillery",
        "ui.icon.unit_role.logistics",
        "ui.icon.unit_role.repair",
        "ui.icon.building_role.command",
        "ui.icon.building_role.extraction",
        "ui.icon.building_role.processing",
        "ui.icon.building_role.factory",
        "ui.icon.building_role.storage",
        "ui.icon.building_role.supply",
        "ui.icon.building_role.power",
        "ui.icon.command.move",
        "ui.icon.command.attack",
        "ui.icon.command.attack_move",
        "ui.icon.command.stop",
        "ui.icon.command.hold",
        "ui.icon.command.patrol",
        "ui.icon.command.build",
        "ui.icon.command.repair",
        "ui.icon.command.supply",
        "ui.icon.command.cancel",
        "ui.icon.cursor.default",
        "ui.icon.cursor.select",
        "ui.icon.cursor.move",
        "ui.icon.cursor.attack",
        "ui.icon.cursor.attack_move",
        "ui.icon.cursor.build",
        "ui.icon.cursor.repair",
        "ui.icon.cursor.supply",
        "ui.icon.cursor.invalid",
        "ui.icon.cursor.pan",
        "ui.icon.cursor.drag_select",
        "ui.icon.supply.supplied",
        "ui.icon.supply.low",
        "ui.icon.supply.critical",
        "ui.icon.supply.unsupplied",
        "ui.icon.minimap.friendly_unit",
        "ui.icon.minimap.visible_enemy",
        "ui.icon.minimap.detected_contact",
        "ui.icon.minimap.building",
        "ui.icon.minimap.command_structure",
        "ui.icon.minimap.resource",
        "ui.icon.minimap.depot",
        "ui.icon.minimap.objective",
        "ui.icon.minimap.selected_group",
        "ui.icon.minimap.attack_notification",
        "ui.icon.status.health",
        "ui.icon.status.fuel",
        "ui.icon.status.ammunition",
        "ui.icon.status.power",
        "ui.icon.status.alert"
    ];

    [Fact]
    public void RtsUiSemanticIconsCompileAndLoadThroughRuntimeCatalog()
    {
        string repositoryRoot = FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-rts-ui-" +
                Guid.NewGuid().ToString("N"));

        try
        {
            AssetCompilationResult result =
                AssetPipelineCompiler.Compile(
                    Path.Combine(repositoryRoot, "assets", "source"),
                    runtimeRoot,
                    clean: true);

            Assert.True(
                result.Success,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(
                        static diagnostic =>
                            $"{diagnostic.Code}: {diagnostic.Message}")));
            Assert.Equal(275, result.CompiledCount);
            Assert.Equal(61, RequiredIds.Length);
            Assert.Equal(
                RequiredIds.Length,
                RequiredIds.Distinct(StringComparer.Ordinal).Count());

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(runtimeRoot);

            foreach (string rawId in RequiredIds)
            {
                RuntimeAssetRecord record =
                    catalog.Get(AssetId.Parse(rawId));

                Assert.Equal(RuntimeAssetType.Material, record.Type);
                Assert.StartsWith(
                    "ui/",
                    record.SourcePath,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(runtimeRoot))
            {
                Directory.Delete(runtimeRoot, recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ForgeLine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test host.");
    }
}
