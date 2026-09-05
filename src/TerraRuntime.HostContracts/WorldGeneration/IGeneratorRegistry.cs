using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.HostContracts.WorldGeneration;

public enum TerraRuntimeWorldGeneratorRegistrationResult : byte
{
    Registered = 0,
    DuplicateId = 1,
    InvalidProvider = 2
}

/// <summary>
/// Lifetime handle for one host-level world generator registration. Hosts retire registrations before unloading the
/// module that owns the provider instance.
/// </summary>
public interface ITerraRuntimeWorldGeneratorRegistration : IDisposable
{
    WorldGeneratorId Id { get; }
    bool IsRetired { get; }
}

/// <summary>
/// Trusted-host registration surface for selectable world generators. Discovery remains a host concern; TerraRuntime
/// receives concrete providers through this explicit contract and never scans assemblies for implementations.
/// </summary>
public interface IGeneratorRegistry
{
    TerraRuntimeWorldGeneratorRegistrationResult TryRegister(
        IWorldGenerationProvider provider,
        out ITerraRuntimeWorldGeneratorRegistration? registration);
}

/// <summary>Read-only provider view used by the runtime bootstrap after trusted modules have registered generators.</summary>
public interface ITerraRuntimeWorldGeneratorSource
{
    ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds();
    bool TryResolveWorldGenerator(WorldGeneratorId id, out IWorldGenerationProvider? provider);
}
