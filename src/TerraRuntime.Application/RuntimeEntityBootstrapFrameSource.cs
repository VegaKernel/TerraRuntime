using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal enum RuntimeEntityBootstrapCaptureResult : byte
{
    Captured = 0,
    InvalidEntityState = 1,
    EncodingFailure = 2,
    FrameBudgetExceeded = 3
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
        if (items.Capacity < 0 || items.Capacity > PlayerBootstrapFrameBudget.MaximumWorldItemSlots)
        {
            throw new ArgumentOutOfRangeException(
                nameof(items),
                $"World-item snapshot capacity must be between 0 and {PlayerBootstrapFrameBudget.MaximumWorldItemSlots}.");
        }

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
            if (result != WorldItemBootstrapPacketEncodeResult.Encoded)
            {
                return result == WorldItemBootstrapPacketEncodeResult.InvalidItemState
                    ? RuntimeEntityBootstrapCaptureResult.InvalidEntityState
                    : RuntimeEntityBootstrapCaptureResult.EncodingFailure;
            }

            if (frames.Length > PlayerBootstrapFrameBudget.MaximumDynamicEntityFrames)
            {
                frames = [];
                return RuntimeEntityBootstrapCaptureResult.FrameBudgetExceeded;
            }

            return RuntimeEntityBootstrapCaptureResult.Captured;
        }
        finally
        {
            ArrayPool<WorldItemSnapshot>.Shared.Return(buffer, clearArray: false);
        }
    }
}
