using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Application;

/// <summary>
/// Startup-only composite over runtime-owned and trusted-host world generators. Built-in IDs are reserved and cannot
/// be shadowed by host modules; discovery itself remains entirely outside the runtime core.
/// </summary>
internal sealed class StartupWorldGeneratorSource : ITerraRuntimeWorldGeneratorSource
{
    private readonly ITerraRuntimeWorldGeneratorSource builtIn;
    private readonly ITerraRuntimeWorldGeneratorSource? host;

    public StartupWorldGeneratorSource(ITerraRuntimeWorldGeneratorSource? host)
    {
        builtIn = BuiltInWorldGeneratorSource.Instance;
        this.host = host;
    }

    public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds()
    {
        var ids = new SortedSet<WorldGeneratorId>();
        foreach (WorldGeneratorId id in builtIn.CaptureWorldGeneratorIds().Span)
            ids.Add(id);

        if (host is not null)
        {
            foreach (WorldGeneratorId id in host.CaptureWorldGeneratorIds().Span)
                ids.Add(id);
        }

        return ids.ToArray();
    }

    public bool TryResolveWorldGenerator(WorldGeneratorId id, out IWorldGenerationProvider? provider)
    {
        if (builtIn.TryResolveWorldGenerator(id, out provider) && provider is not null)
            return true;

        if (host is not null && host.TryResolveWorldGenerator(id, out provider) && provider is not null)
            return true;

        provider = null;
        return false;
    }
}
