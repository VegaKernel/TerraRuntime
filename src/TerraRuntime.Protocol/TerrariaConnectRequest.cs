namespace TerraRuntime.Protocol;

public readonly record struct TerrariaConnectRequest(int ProtocolRelease)
{
    public bool IsCurrentProtocol => TerrariaProtocolVersion.IsCurrent(ProtocolRelease);
}
