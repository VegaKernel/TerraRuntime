namespace TerraRuntime.Protocol;

public enum TerrariaFrameReadResult : byte
{
    Frame = 0,
    NeedMoreData = 1,
    InvalidLength = 2,
    FrameTooLarge = 3
}
