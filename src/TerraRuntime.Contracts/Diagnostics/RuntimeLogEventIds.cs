namespace TerraRuntime.Contracts.Diagnostics;

/// <summary>
/// Reserved event-ID ranges. Concrete events must stay inside the owning subsystem range so IDs remain
/// stable across message-text changes.
/// </summary>
public static class RuntimeLogEventIds
{
    public const int LifecycleBase = 1000;
    public const int NetworkBase = 2000;
    public const int ProtocolBase = 3000;
    public const int WorldBase = 4000;
    public const int PersistenceBase = 5000;
    public const int PluginBase = 6000;
    public const int GameplayBase = 7000;
    public const int OperationsBase = 8000;
    public const int SecurityBase = 9000;

    public static readonly RuntimeLogEventId LifecycleInformation = new(LifecycleBase);
    public static readonly RuntimeLogEventId LifecycleWarning = new(LifecycleBase + 1);
    public static readonly RuntimeLogEventId LifecycleError = new(LifecycleBase + 2);
}
