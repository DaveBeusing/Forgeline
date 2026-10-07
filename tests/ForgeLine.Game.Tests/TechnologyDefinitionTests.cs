using ForgeLine.Core;
using ForgeLine.Economy;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyDefinitionTests
{
    [Fact]
    public void DirectorateCatalogUsesStableIdsAndAllTechnologyDomains()
    {
        TechnologyDefinitionCatalog catalog =
            DirectorateTechnologyDefinitions.CreateCatalog();

        Assert.Equal(
            4,
            catalog.Count);
        Assert.Equal(
            4,
            catalog.Definitions
                .Select(
                    static definition =>
                        definition.Id)
                .Distinct()
                .Count());
        Assert.Equal(
            Enum.GetValues<TechnologyDomain>()
                .Order()
                .ToArray(),
            catalog.Definitions
                .Select(
                    static definition =>
                        definition.Domain)
                .Distinct()
                .Order()
                .ToArray());

        Assert.True(
            catalog.TryResolve(
                "directorate.technology.industrial_standardization",
                out TechnologyId industrial));
        Assert.Equal(
            TechnologyIds.IndustrialStandardization,
            industrial);
    }

    [Fact]
    public void RepresentativePathCarriesPhysicalAndInfrastructureRequirements()
    {
        TechnologyDefinitionCatalog catalog =
            DirectorateTechnologyDefinitions.CreateCatalog();

        TechnologyDefinition industrial =
            catalog[
                TechnologyIds.IndustrialStandardization];
        TechnologyDefinition mechanized =
            catalog[
                TechnologyIds.MechanizedSystems];

        Assert.Equal(
            BuildingIds.CommandCore,
            industrial.RequiredFacility);
        Assert.Equal(
            1.0,
            industrial.RequiredPowerFraction);
        Assert.Contains(
            industrial.Costs,
            static cost =>
                cost.ResourceId ==
                    ResourceIds.Steel &&
                cost.Quantity ==
                    80.0);
        Assert.Contains(
            TechnologyCapabilityIds.FieldEngineering,
            industrial.Unlocks);

        Assert.Contains(
            TechnologyIds.IndustrialStandardization,
            mechanized.Prerequisites);
        Assert.Equal(
            BuildingIds.VehicleFactory,
            mechanized.RequiredFacility);
    }

    [Fact]
    public void CatalogRejectsUnknownPrerequisites()
    {
        var definition =
            new TechnologyDefinition
            {
                Id = new TechnologyId(100),
                Key = "technology.invalid",
                DisplayName = "Invalid",
                Domain = TechnologyDomain.Industry,
                Phase = TechnologyPhase.Bootstrap,
                ResearchTicks = 10,
                Costs =
                [
                    new TechnologyResourceCost(
                        ResourceIds.Steel,
                        1.0)
                ],
                Prerequisites =
                [
                    new TechnologyId(999)
                ],
                RequiredFacility =
                    BuildingIds.CommandCore
            };

        Assert.Throws<ArgumentException>(
            () =>
                new TechnologyDefinitionCatalog(
                    [definition]));
    }
}
