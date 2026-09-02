using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;

namespace TerraRuntime;

/// <summary>
/// Retained per-connection replication state. Socket lifetime stays outside this type; it owns the currently playing
/// exact-generation identity plus bounded appearance/equipment/movement baselines used by runtime fanout.
/// </summary>
internal sealed class RuntimeConnectionEndpoint
{
    private readonly object equipmentGate = new();
    private readonly SortedDictionary<short, byte[]> equipmentFrames = [];
    private int playingSlot = -1;
    private ulong playingGeneration;
    private int appearanceSlot = -1;
    private int equipmentOwnerSlot = -1;
    private bool hasPosition;
    private float positionX;
    private float positionY;
    private byte[]? latestAppearanceFrame;
    private byte[]? latestMovementFrame;

    public RuntimeConnectionEndpoint(TerrariaConnectionOutboundQueue outbound)
    {
        Outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
    }

    public TerrariaConnectionOutboundQueue Outbound { get; }

    public void MarkPlaying(PlayerHandle player)
    {
        Volatile.Write(ref playingGeneration, player.Generation.Value);
        Volatile.Write(ref playingSlot, player.Slot.Value);
    }

    public bool TryGetPlayingPlayer(out PlayerHandle player)
    {
        int slotValue = Volatile.Read(ref playingSlot);
        ulong generation = Volatile.Read(ref playingGeneration);
        if (slotValue < 0 ||
            generation == 0 ||
            slotValue != Volatile.Read(ref playingSlot))
        {
            player = default;
            return false;
        }

        player = new PlayerHandle(
            new PlayerSlotId(checked((byte)slotValue)),
            new PlayerSessionGeneration(generation));
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

    public void UpdateLatestAppearanceFrame(PlayerSlotId slot, byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        Volatile.Write(ref latestAppearanceFrame, encoded);
        Volatile.Write(ref appearanceSlot, slot.Value);
    }

    public bool TryGetLatestAppearanceFrame(PlayerSlotId expectedSlot, out OutboundFrame frame)
    {
        int currentAppearanceSlot = Volatile.Read(ref appearanceSlot);
        byte[]? encoded = Volatile.Read(ref latestAppearanceFrame);
        if (currentAppearanceSlot != expectedSlot.Value || encoded is null)
        {
            frame = default;
            return false;
        }

        frame = new OutboundFrame(encoded);
        return true;
    }

    public bool UpdateLatestEquipmentFrame(PlayerSlotId ownerSlot, short equipmentSlot, byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        lock (equipmentGate)
        {
            if (equipmentOwnerSlot != ownerSlot.Value)
            {
                equipmentFrames.Clear();
                equipmentOwnerSlot = ownerSlot.Value;
            }

            if (equipmentFrames.ContainsKey(equipmentSlot))
            {
                equipmentFrames[equipmentSlot] = encoded;
                return true;
            }

            if (equipmentFrames.Count >= VanillaPlayerItemSlotCatalog.RelayableCount)
                return false;

            equipmentFrames.Add(equipmentSlot, encoded);
            return true;
        }
    }

    public int EnqueueEquipmentBaselineTo(RuntimeConnectionEndpoint recipient, PlayerSlotId expectedOwnerSlot)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        int enqueued = 0;
        lock (equipmentGate)
        {
            if (equipmentOwnerSlot != expectedOwnerSlot.Value)
                return 0;

            foreach (byte[] encoded in equipmentFrames.Values)
            {
                if (recipient.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
                    enqueued++;
            }
        }

        return enqueued;
    }

    public void UpdateLatestMovementFrame(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        Volatile.Write(ref latestMovementFrame, encoded);
    }

    public bool TryGetLatestMovementFrame(out OutboundFrame frame)
    {
        byte[]? encoded = Volatile.Read(ref latestMovementFrame);
        if (encoded is null)
        {
            frame = default;
            return false;
        }

        frame = new OutboundFrame(encoded);
        return true;
    }

    public RuntimePlayerInterestState CreateInterestState(PlayerSlotId slot) =>
        new(slot, hasPosition, positionX, positionY);

    public void ClearPlaying(PlayerHandle player)
    {
        if (Volatile.Read(ref playingGeneration) != player.Generation.Value ||
            Interlocked.CompareExchange(ref playingSlot, -1, player.Slot.Value) != player.Slot.Value)
        {
            return;
        }

        Volatile.Write(ref playingGeneration, 0);
        hasPosition = false;
        if (Interlocked.CompareExchange(ref appearanceSlot, -1, player.Slot.Value) == player.Slot.Value)
            Volatile.Write(ref latestAppearanceFrame, null);

        lock (equipmentGate)
        {
            if (equipmentOwnerSlot == player.Slot.Value)
            {
                equipmentOwnerSlot = -1;
                equipmentFrames.Clear();
            }
        }

        Volatile.Write(ref latestMovementFrame, null);
    }
}
