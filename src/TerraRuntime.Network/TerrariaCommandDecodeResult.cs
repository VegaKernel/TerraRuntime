namespace TerraRuntime.Network;

public enum TerrariaCommandDecodeResult : byte
{
    Ignored = 0,
    Decoded = 1,
    Malformed = 2
}
