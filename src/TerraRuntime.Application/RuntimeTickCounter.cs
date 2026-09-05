namespace TerraRuntime.Application;

/// <summary>
/// Owns the monotonic authoritative simulation-update index for one live world. Only the world writer advances it;
/// collaborators receive a read-only provider so they cannot mutate tick ordering.
/// </summary>
internal sealed class RuntimeTickCounter
{
    public long Current { get; private set; }

    public void Advance() => Current++;
}
