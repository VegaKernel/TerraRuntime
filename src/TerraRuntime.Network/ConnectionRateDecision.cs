namespace TerraRuntime.Network;

public enum ConnectionRateDecision : byte
{
    Allowed = 0,
    FrameLimitExceeded = 1,
    ByteLimitExceeded = 2
}
