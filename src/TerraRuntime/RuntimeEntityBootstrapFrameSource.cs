using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal enum RuntimeEntityBootstrapCaptureResult : byte
{
    Captured = 0,
    InvalidEntityState = 1,
    EncodingFailure = 2
}

/// <summary>
/// Captures dynamic authoritative entity state at join time. Static world/section frames stay cached in
/// <see cref="PlayerBootstrapPacketSet"/>; mutable items, projectiles and future runtime NPC state belong here.
/// </summary>
internal sealed class RuntimeEntityBootstrapFrameSource
{
    private readonly IWorldItemSnapshotReader _items;

    public RuntimeEntityBootstrapFrameSource(IWorldItemSnapshotReader items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(items), "World-item snapshot capacity cannot be negative.");

        _items = items;
    }

    public RuntimeEntityBootstrapCaptureResult TryCapture(out ReadOnlyMemory<byte>[] frames)
    {
        if (_items.Capacity == 0)
        {
            frames = [];
            return RuntimeEntityBootstrapCaptureResult.Captured;
        }

        WorldItemSnapshot[] buffer = ArrayPool<WorldItemSnapshot>.Shared.Rent(_items.Capacity);
        try
        {
            int count;
            try
            {
                count = _items.CopyActive(buffer.AsSpan(0, _items.Capacity));
            }
            catch (ArgumentException)
            {
                frames = [];
                return RuntimeEntityBootstrapCaptureResult.InvalidEntityState;
            }

            if ((uint)count > (uint)_items.Capacity)
            {
                frames = [];
                return RuntimeEntityBootstrapCaptureResult.InvalidEntityState;
            }

            WorldItemBootstrapPacketEncodeResult result = WorldItemBootstrapPacketEncoder.TryEncode(
                buffer.AsSpan(0, count),
                out frames);
            return result switch
            {
                WorldItemBootstrapPacketEncodeResult.Encoded => RuntimeEntityBootstrapCaptureResult.Captured,
                WorldItemBootstrapPacketEncodeResult.InvalidItemState => RuntimeEntityBootstrapCaptureResult.InvalidEntityState,
                _ => RuntimeEntityBootstrapCaptureResult.EncodingFailure
            };
        }
        finally
        {
            ArrayPool<WorldItemSnapshot>.Shared.Return(buffer, clearArray: false);
        }
    }
}
