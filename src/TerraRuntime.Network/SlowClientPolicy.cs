namespace TerraRuntime.Network;

public enum SlowClientPolicy : byte
{
    DisconnectOnQueueOverflow = 0,
    RejectNewest = 1
}
