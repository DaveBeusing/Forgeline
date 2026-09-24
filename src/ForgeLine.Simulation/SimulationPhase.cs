namespace ForgeLine.Simulation;

public enum SimulationPhase
{
    InputCommands = 0,
    OrderProcessing = 1,
    AiDecisions = 2,
    NavigationRequests = 3,
    Movement = 4,
    Sensors = 5,
    Combat = 6,
    DamageResolution = 7,
    Supply = 8,
    Logistics = 9,
    Production = 10,
    Economy = 11,
    EntityLifecycle = 12,
    SnapshotEvents = 13,
}
