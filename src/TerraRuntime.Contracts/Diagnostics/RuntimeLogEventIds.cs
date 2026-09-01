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

    // Live-host lifecycle events. Values 1000-1002 remain the generic lifecycle seed IDs above.
    public static readonly RuntimeLogEventId StartupProfile = new(LifecycleBase + 10);
    public static readonly RuntimeLogEventId ShutdownCommandDrainTimedOut = new(LifecycleBase + 11);
    public static readonly RuntimeLogEventId GameLoopStopTimedOut = new(LifecycleBase + 12);

    // Live-host network events.
    public static readonly RuntimeLogEventId NetworkListenerReady = new(NetworkBase);
    public static readonly RuntimeLogEventId NetworkListenerStartFailed = new(NetworkBase + 1);
    public static readonly RuntimeLogEventId NetworkAcceptFailed = new(NetworkBase + 2);
    public static readonly RuntimeLogEventId NetworkConnectionAccepted = new(NetworkBase + 3);
    public static readonly RuntimeLogEventId NetworkConnectionStopped = new(NetworkBase + 4);
    public static readonly RuntimeLogEventId NetworkConnectionFailed = new(NetworkBase + 5);
    public static readonly RuntimeLogEventId NetworkShutdownFault = new(NetworkBase + 6);
    public static readonly RuntimeLogEventId NetworkDisconnectEnqueueFailed = new(NetworkBase + 7);

    // Live-host world/bootstrap events.
    public static readonly RuntimeLogEventId WorldFileMissing = new(WorldBase);
    public static readonly RuntimeLogEventId WorldSourceStatFailed = new(WorldBase + 1);
    public static readonly RuntimeLogEventId WorldSourceRestatFailed = new(WorldBase + 2);
    public static readonly RuntimeLogEventId WorldCacheHit = new(WorldBase + 3);
    public static readonly RuntimeLogEventId WorldCacheMiss = new(WorldBase + 4);
    public static readonly RuntimeLogEventId WorldReadFailed = new(WorldBase + 5);
    public static readonly RuntimeLogEventId WorldLoadFailed = new(WorldBase + 6);
    public static readonly RuntimeLogEventId WorldRecoverySuppressed = new(WorldBase + 7);
    public static readonly RuntimeLogEventId WorldCheckpointRecoveryFailed = new(WorldBase + 8);
    public static readonly RuntimeLogEventId WorldCheckpointRecovered = new(WorldBase + 9);
    public static readonly RuntimeLogEventId WorldBootstrapCacheHit = new(WorldBase + 10);
    public static readonly RuntimeLogEventId WorldBootstrapPreparationFailed = new(WorldBase + 11);

    // Live-host persistence events.
    public static readonly RuntimeLogEventId PersistenceCanonicalCleanupFailed = new(PersistenceBase);
    public static readonly RuntimeLogEventId PersistenceBackupCleanupFailed = new(PersistenceBase + 1);
    public static readonly RuntimeLogEventId PersistenceWorldCacheRebuilt = new(PersistenceBase + 2);
    public static readonly RuntimeLogEventId PersistenceWorldCacheWriteFailed = new(PersistenceBase + 3);
    public static readonly RuntimeLogEventId PersistenceSaveTemplateLoadFailed = new(PersistenceBase + 4);
    public static readonly RuntimeLogEventId PersistenceSaveTemplateReady = new(PersistenceBase + 5);
    public static readonly RuntimeLogEventId PersistenceBootstrapCacheRebuilt = new(PersistenceBase + 6);
    public static readonly RuntimeLogEventId PersistenceBootstrapCacheWriteFailed = new(PersistenceBase + 7);
    public static readonly RuntimeLogEventId PersistenceWorldCheckpointCommitted = new(PersistenceBase + 8);
    public static readonly RuntimeLogEventId PersistenceWorldCheckpointSaveFailed = new(PersistenceBase + 9);
    public static readonly RuntimeLogEventId PersistenceWorldCheckpointSuppressedByLoopFault = new(PersistenceBase + 10);
    public static readonly RuntimeLogEventId PersistenceRuntimeCacheInvalidationFailed = new(PersistenceBase + 11);

    // Trusted host-module lifecycle is a plugin/host integration concern rather than generic runtime lifecycle.
    public static readonly RuntimeLogEventId PluginHostRuntimeAttachFailed = new(PluginBase);
    public static readonly RuntimeLogEventId PluginHostRuntimeDetachFailed = new(PluginBase + 1);

    // Operations IDs 8000-8002 belonged to the transitional L3 delivery bridge and are permanently retired.
    // Stable event IDs are never recycled for unrelated semantics.
    public static readonly RuntimeLogEventId OperationsTerminalUiFailed = new(OperationsBase + 3);
    public static readonly RuntimeLogEventId OperationsReadModelMessage = new(OperationsBase + 4);
    public static readonly RuntimeLogEventId OperationsSandboxJobCompleted = new(OperationsBase + 5);
    public static readonly RuntimeLogEventId OperationsSandboxJobFailed = new(OperationsBase + 6);
}
