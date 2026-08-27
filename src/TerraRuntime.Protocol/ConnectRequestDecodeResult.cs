namespace TerraRuntime.Protocol;

public enum ConnectRequestDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    MalformedPayload = 2,
    InvalidVersionBanner = 3
}
