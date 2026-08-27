namespace TerraRuntime.Network;

public enum TerrariaCommandFrameSinkStopReason : byte
{
    None = 0,
    MalformedCommand = 1,
    GameLoopBackpressure = 2
}
