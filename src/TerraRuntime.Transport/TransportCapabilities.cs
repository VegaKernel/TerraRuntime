namespace TerraRuntime.Transport;

[Flags]
public enum TransportCapabilities : ulong
{
    None = 0,
    RequestResponse = 1UL << 0,
    Events = 1UL << 1,
    Heartbeats = 1UL << 2,
    Cancellation = 1UL << 3,
    Compression = 1UL << 4,
    SharedMemorySnapshots = 1UL << 5
}
