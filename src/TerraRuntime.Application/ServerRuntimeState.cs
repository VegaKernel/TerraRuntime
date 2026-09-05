using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Authoritative single-writer facade for one running world. Mutable counters and orchestration stay here;
/// subsystem construction/ownership is isolated in <see cref="ServerRuntimeComposition"/>.
/// </summary>
internal sealed partial class ServerRuntimeState
{
    private readonly ServerRuntimeComposition _runtime;
    private int lastWorkerResult;
}
