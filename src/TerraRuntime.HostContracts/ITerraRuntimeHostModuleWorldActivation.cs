namespace TerraRuntime.HostContracts;

/// <summary>
/// Optional trusted-module policy for per-world activation. TerraRuntime asks before runtime attachment; configuration
/// and policy remain module-owned, while skipped worlds receive no runtime scope or mutable registrations.
/// </summary>
public interface ITerraRuntimeHostModuleWorldActivation
{
    bool IsEnabledForWorld(TerraRuntimeHostRuntimeInfo world);
}
