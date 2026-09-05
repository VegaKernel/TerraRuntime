using TerraRuntime.Gameplay.Items;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

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

    // PlayerSpawned publishes the current runtime entity baselines to the joining connection. NPC bootstrap includes
    // the ordinary NPC sync plus independently cached town identity and home packets. The previous sizing counted
    // only the ordinary NPC frame and could therefore classify a healthy join as a slow client when the world had
    // enough persisted town-NPC metadata.
    public const int MaximumRuntimeEntityBaselineFrames =
        RuntimeNpcStore.MaximumAddressableCapacity +
        (RuntimeTownNpcStateStore.MaximumTownNpcs * 2) +
        RuntimeProjectileStore.MaximumProtocolAddressableCapacity;

    // Every other occupied player slot is either another connection or a server-owned player. A connected peer can
    // contribute active + appearance + relayable equipment + health + mana. A server-owned player adds movement too,
    // so use that slightly larger envelope for every non-origin slot instead of under-sizing by ownership kind.
    public const int MaximumOtherPlayerBaselineFramesPerSlot =
        2 + VanillaPlayerItemSlotCatalog.RelayableCount + 3;

    public const int DefaultPlayerCount = ServerHostOptions.DefaultMaxPlayers;

    public const int DefaultStructuralFrameBudget =
        MaximumInitialJoinFrames +
        MaximumRuntimeEntityBaselineFrames +
        ((DefaultPlayerCount - 1) * MaximumOtherPlayerBaselineFramesPerSlot);

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
            ((maxPlayers - 1) * MaximumOtherPlayerBaselineFramesPerSlot));

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
