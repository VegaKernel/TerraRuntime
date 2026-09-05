namespace TerraRuntime.Application;

/// <summary>
/// Owns the count of authoritative commands applied by one live world. The world writer records ingress commands;
/// authoritative nested command-equivalent transactions record through the same counter without re-entering the
/// top-level runtime facade.
/// </summary>
internal sealed class RuntimeCommandCounter
{
    public long Current { get; private set; }

    public void Record() => Current++;
}
