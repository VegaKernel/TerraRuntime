namespace TerraRuntime.Protocol;

public readonly record struct TerrariaFrameDecoderOptions
{
    public const int MinimumFrameLength = 3;
    public const int AbsoluteMaximumFrameLength = ushort.MaxValue;

    public TerrariaFrameDecoderOptions(int maxFrameLength)
    {
        if (maxFrameLength is < MinimumFrameLength or > AbsoluteMaximumFrameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFrameLength),
                maxFrameLength,
                $"Frame length must be between {MinimumFrameLength} and {AbsoluteMaximumFrameLength} bytes.");
        }

        MaxFrameLength = maxFrameLength;
    }

    public int MaxFrameLength { get; }

    public static TerrariaFrameDecoderOptions Default { get; } = new(AbsoluteMaximumFrameLength);
}
