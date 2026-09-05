using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;

namespace TerraRuntime.Application;

/// <summary>
/// Retained per-connection replication state. Socket lifetime stays outside this type; it owns the currently playing
/// exact-generation identity plus bounded appearance/equipment/movement baselines used by runtime fanout.
/// Every retained player baseline is tagged with the exact <see cref="PlayerHandle"/> that produced it so slot reuse
/// cannot make a stale generation visible to a later player session.
/// </summary>
internal sealed class RuntimeConnectionEndpoint
{
    private readonly object equipmentGate = new();
    private readonly SortedDictionary<short, byte[]> equipmentFrames = [];
    private PlayingIdentity? playing;
    private PlayerHandle? equipmentOwner;
    private bool hasPosition;
    private float positionX;
    private float positionY;
    private RetainedFrame? latestAppearance;
    private RetainedFrame? latestMovement;

    public RuntimeConnectionEndpoint(TerrariaConnectionOutboundQueue outbound)
    {
        Outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
    }

    public TerrariaConnectionOutboundQueue Outbound { get; }

    public void MarkPlaying(PlayerHandle player)
    {
        if (!player.IsAssigned)
            throw new ArgumentException("Playing identity must be assigned.", nameof(player));

        Volatile.Write(ref playing, new PlayingIdentity(player));
    }

    public bool TryGetPlayingPlayer(out PlayerHandle player)
    {
        PlayingIdentity? current = Volatile.Read(ref playing);
        if (current is null)
        {
            player = default;
            return false;
        }

        player = current.Player;
        return true;
    }

    public bool TryGetPlayingSlot(out PlayerSlotId slot)
    {
        if (!TryGetPlayingPlayer(out PlayerHandle player))
        {
            slot = default;
            return false;
        }

        slot = player.Slot;
        return true;
    }

    public void UpdatePosition(float positionX, float positionY)
    {
        this.positionX = positionX;
        this.positionY = positionY;
        hasPosition = true;
    }

    public bool UpdateLatestAppearanceFrame(PlayerHandle owner, byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (!owner.IsAssigned)
            throw new ArgumentException("Appearance baseline owner must be assigned.", nameof(owner));

        RetainedFrame? current = Volatile.Read(ref latestAppearance);
        if (current is not null && current.Owner == owner && current.Encoded.AsSpan().SequenceEqual(encoded))
            return false;

        Volatile.Write(ref latestAppearance, new RetainedFrame(owner, encoded));
        return true;
    }

    public bool TryGetLatestAppearanceFrame(PlayerHandle expectedOwner, out OutboundFrame frame) =>
        TryGetRetainedFrame(Volatile.Read(ref latestAppearance), expectedOwner, out frame);

    public bool UpdateLatestEquipmentFrame(PlayerHandle owner, short equipmentSlot, byte[] encoded) =>
        UpdateLatestEquipmentFrame(owner, equipmentSlot, encoded, out _);

    public bool UpdateLatestEquipmentFrame(
        PlayerHandle owner,
        short equipmentSlot,
        byte[] encoded,
        out bool changed)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        changed = false;
        if (!owner.IsAssigned)
            return false;

        lock (equipmentGate)
        {
            if (equipmentOwner != owner)
            {
                equipmentFrames.Clear();
                equipmentOwner = owner;
            }

            if (equipmentFrames.TryGetValue(equipmentSlot, out byte[]? current))
            {
                if (current.AsSpan().SequenceEqual(encoded))
                    return true;

                equipmentFrames[equipmentSlot] = encoded;
                changed = true;
                return true;
            }

            if (equipmentFrames.Count >= VanillaPlayerItemSlotCatalog.RelayableCount)
                return false;

            equipmentFrames.Add(equipmentSlot, encoded);
            changed = true;
            return true;
        }
    }

    public int EnqueueEquipmentBaselineTo(RuntimeConnectionEndpoint recipient, PlayerHandle expectedOwner)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        int enqueued = 0;
        lock (equipmentGate)
        {
            if (equipmentOwner != expectedOwner)
                return 0;

            foreach (byte[] encoded in equipmentFrames.Values)
            {
                if (recipient.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
                    enqueued++;
            }
        }

        return enqueued;
    }

    public bool UpdateLatestMovementFrame(PlayerHandle owner, byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (!owner.IsAssigned)
            throw new ArgumentException("Movement baseline owner must be assigned.", nameof(owner));

        RetainedFrame? current = Volatile.Read(ref latestMovement);
        if (current is not null && current.Owner == owner && current.Encoded.AsSpan().SequenceEqual(encoded))
            return false;

        Volatile.Write(ref latestMovement, new RetainedFrame(owner, encoded));
        return true;
    }

    public bool TryGetLatestMovementFrame(PlayerHandle expectedOwner, out OutboundFrame frame) =>
        TryGetRetainedFrame(Volatile.Read(ref latestMovement), expectedOwner, out frame);

    public void ClearLatestMovementFrame(PlayerHandle owner) =>
        ClearRetainedFrame(ref latestMovement, owner);

    public RuntimePlayerInterestState CreateInterestState(PlayerSlotId slot) =>
        new(slot, hasPosition, positionX, positionY);

    public void ClearPlaying(PlayerHandle player)
    {
        PlayingIdentity? current = Volatile.Read(ref playing);
        if (current is null ||
            current.Player != player ||
            Interlocked.CompareExchange(ref playing, null, current) != current)
        {
            return;
        }

        hasPosition = false;
        ClearRetainedFrame(ref latestAppearance, player);
        ClearRetainedFrame(ref latestMovement, player);

        lock (equipmentGate)
        {
            if (equipmentOwner == player)
            {
                equipmentOwner = null;
                equipmentFrames.Clear();
            }
        }
    }

    private static bool TryGetRetainedFrame(
        RetainedFrame? retained,
        PlayerHandle expectedOwner,
        out OutboundFrame frame)
    {
        if (retained is null || retained.Owner != expectedOwner)
        {
            frame = default;
            return false;
        }

        frame = new OutboundFrame(retained.Encoded);
        return true;
    }

    private static void ClearRetainedFrame(ref RetainedFrame? location, PlayerHandle owner)
    {
        RetainedFrame? current = Volatile.Read(ref location);
        if (current is not null && current.Owner == owner)
            Interlocked.CompareExchange(ref location, null, current);
    }

    private sealed class PlayingIdentity(PlayerHandle player)
    {
        public PlayerHandle Player { get; } = player;
    }

    private sealed class RetainedFrame(PlayerHandle owner, byte[] encoded)
    {
        public PlayerHandle Owner { get; } = owner;

        public byte[] Encoded { get; } = encoded ?? throw new ArgumentNullException(nameof(encoded));
    }
}
