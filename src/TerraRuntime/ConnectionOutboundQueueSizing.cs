using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime;

/// <summary>
/// Derives the per-connection outbound queue envelope from the largest synchronous join/baseline bursts the
/// production composition can emit. This replaces the old fixed 4096-frame ceiling, which only happened to
/// cover the default eight-player configuration.
/// </summary>
internal static class ConnectionOutboundQueueSizing
{
    // One normal one-pass join can enqueue these control frames outside tile sections:
    // packet 3, the first packet 7, the packet-8 response packet 7 + packet 9 + packet 49,
    // and packet 129 after the authoritative spawn request.
    public const int InitialJoinControlFrames = 6;

    public const int MaximumInitialJoinFrames =
        InitialJoinControlFrames + PlayerBootstrapFrameBudget.MaximumTileSectionFrames;

    // PlayerSpawned publishes the current runtime entity baselines to the joining connection.
    public const int MaximumRuntimeEntityBaselineFrames =
        RuntimeNpcStore.MaximumAddressableCapacity +
        RuntimeProjectileStore.MaximumProtocolAddressableCapacity;

    // For each already-playing peer the joining connection can receive:
    // active + appearance + every relayable equipment slot + health + mana.
    public const int MaximumPlayerBaselineFramesPerPeer =
        2 + VanillaPlayerItemSlotCatalog.RelayableCount + 2;

    public const int DefaultPlayerCount = ServerHostOptions.DefaultMaxPlayers;

    public const int DefaultStructuralFrameBudget =
        MaximumInitialJoinFrames +
        MaximumRuntimeEntityBaselineFrames +
        ((DefaultPlayerCount - 1) * MaximumPlayerBaselineFramesPerPeer);

    // Preserve the deployed eight-player byte envelope while frame capacity becomes structural. Until queue
    // high-water measurements replace this baseline, scale bytes linearly with the same structural workload.
    public const long DefaultByteBudget = 16L * 1024 * 1024;

    public static OutboundQueueOptions Create(int maxPlayers)
    {
        if (maxPlayers <= 0 || maxPlayers > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers));

        int maxFrames = checked(
            MaximumInitialJoinFrames +
            MaximumRuntimeEntityBaselineFrames +
            ((maxPlayers - 1) * MaximumPlayerBaselineFramesPerPeer));

        long scaledBytes = checked(
            ((DefaultByteBudget * maxFrames) + DefaultStructuralFrameBudget - 1) /
            DefaultStructuralFrameBudget);
        long maxQueuedBytes = Math.Max(
            TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength,
            scaledBytes);

        return new OutboundQueueOptions(
            maxFrames,
            maxQueuedBytes,
            TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength);
    }
}
