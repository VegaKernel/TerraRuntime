using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Network;

/// <summary>Owns the shared configuration only; never retains sessions or their counters.</summary>
public sealed class PacketRateLimitControl : IPacketRateLimitControl
{
    private readonly int[] limits = new int[byte.MaxValue + 1];

    public int? GetLimit(byte messageId)
    {
        int value = Volatile.Read(ref limits[messageId]);
        return value == 0 ? null : value;
    }

    public void SetLimit(byte messageId, int? framesPerSecond)
    {
        if (framesPerSecond is int value)
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Volatile.Write(ref limits[messageId], framesPerSecond ?? 0);
    }
}
