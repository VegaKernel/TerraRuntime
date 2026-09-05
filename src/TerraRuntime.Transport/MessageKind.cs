namespace TerraRuntime.Transport;

public enum MessageKind : byte
{
    Handshake = 1,
    Request = 2,
    Response = 3,
    Event = 4,
    Heartbeat = 5,
    Error = 6,
    Cancel = 7
}
