namespace TerraRuntime.Contracts.Diagnostics;

/// <summary>Stable top-level category used for routing and filtering runtime diagnostics.</summary>
public enum RuntimeLogCategory : byte
{
    Lifecycle = 0,
    Network = 1,
    Protocol = 2,
    World = 3,
    Persistence = 4,
    Plugin = 5,
    Gameplay = 6,
    Operations = 7,
    Security = 8
}
