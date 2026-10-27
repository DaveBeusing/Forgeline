using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public readonly record struct ConveyorPortInformation(EntityId Building, bool Input, string Materials);

internal static class ConveyorPortInformationCapture
{
    public static string Materials(MatchRuntime scenario, int mask)
    {
        var names = new List<string>(ConveyorSimulationSystem.EligibleResources.Count);
        for (int index = 0; index < ConveyorSimulationSystem.EligibleResources.Count; index++)
            if ((mask & (1 << index)) != 0)
            {
                var resource = ConveyorSimulationSystem.EligibleResources[index];
                names.Add(scenario.Services.Resources.TryGet(resource, out var definition)
                    ? definition.DisplayName : resource.ToString());
            }
        return names.Count == 0 ? "NONE" : string.Join(", ", names);
    }
}
