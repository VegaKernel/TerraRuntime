namespace TerraRuntime.Tests;

// These gates measure warmed steady-state dispatch, not shared-pool eviction under unrelated world-generation
// pressure. ArrayPool is process-wide: concurrently generating canonical worlds invalidates a fixture's warmup.
// Keep the workloads and byte limits unchanged, but isolate their measurement window from other test collections.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SteadyStateAllocationCollection
{
    public const string Name = "Steady-state allocation measurement";
}
