using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// World-session retained protocol baselines for server-owned players. This store owns exact-generation replica
/// replacement, bounded relayable item snapshots and the representation translation needed to build player frames.
/// Connection selection and queue fanout remain with <see cref="RuntimeConnectionRegistry"/>.
/// </summary>
internal sealed class ServerPlayerReplicaStore
{
    private const int ProtocolPlayerSlotCount = byte.MaxValue + 1;

    private readonly ServerPlayerReplica?[] replicas = new ServerPlayerReplica?[ProtocolPlayerSlotCount];

    public bool TryCreate(in PlayerStateSnapshot player, out byte[] active, out byte[] movement)
    {
        if (!player.Player.IsAssigned)
        {
            active = [];
            movement = [];
            return false;
        }

        active = TerrariaPlayerActiveEncoder.Encode(player.Player.Slot.Value, active: true);
        movement = TerrariaPlayerReplicationFrameEncoder.EncodeMovement(in player);
        replicas[player.Player.Slot.Value] = new ServerPlayerReplica(player.Player, active)
        {
            Movement = movement
        };
        return true;
    }

    public bool TryUpdateAppearance(
        PlayerHandle player,
        in ServerPlayerAppearanceState appearance,
        out byte[] encoded)
    {
        if (!TryGet(player, out ServerPlayerReplica replica))
        {
            encoded = [];
            return false;
        }

        encoded = TerrariaPlayerReplicationFrameEncoder.EncodeAppearance(player.Slot, in appearance);
        replica.Appearance = encoded;
        return true;
    }

    public bool TryUpdateVitals(
        PlayerHandle player,
        in ServerPlayerVitalsState vitals,
        out byte[] health,
        out byte[] mana)
    {
        if (!TryGet(player, out ServerPlayerReplica replica))
        {
            health = [];
            mana = [];
            return false;
        }

        var healthState = new TerrariaPlayerHealthState(player.Slot.Value, vitals.Life, vitals.MaxLife);
        var manaState = new TerrariaPlayerManaState(player.Slot.Value, vitals.Mana, vitals.MaxMana);
        health = TerrariaPlayerVitalsCodec.EncodeHealth(in healthState);
        mana = TerrariaPlayerVitalsCodec.EncodeMana(in manaState);
        replica.Health = health;
        replica.Mana = mana;
        return true;
    }

    public bool TryUpdateItem(PlayerHandle player, in ServerPlayerItemState item, out byte[] encoded)
    {
        if (!VanillaPlayerItemSlotCatalog.CanRelay(item.Slot) ||
            !TryGet(player, out ServerPlayerReplica replica))
        {
            encoded = [];
            return false;
        }

        encoded = TerrariaPlayerReplicationFrameEncoder.EncodeEquipment(player.Slot, in item);
        if (item.IsEmpty)
            replica.Items.Remove(item.Slot);
        else
            replica.Items[item.Slot] = encoded;
        return true;
    }

    public bool TryUpdateMovement(in PlayerStateSnapshot player, out byte[] encoded)
    {
        if (!TryGet(player.Player, out ServerPlayerReplica replica))
        {
            encoded = [];
            return false;
        }

        encoded = TerrariaPlayerReplicationFrameEncoder.EncodeMovement(in player);
        replica.Movement = encoded;
        return true;
    }

    public bool TryRemove(PlayerHandle player, out byte[] inactive)
    {
        if (!TryGet(player, out _))
        {
            inactive = [];
            return false;
        }

        replicas[player.Slot.Value] = null;
        inactive = TerrariaPlayerActiveEncoder.Encode(player.Slot.Value, active: false);
        return true;
    }

    public bool TryGetAppearanceFrame(PlayerHandle player, out OutboundFrame frame) =>
        TryGetFrame(player, static replica => replica.Appearance, out frame);

    public bool TryGetHealthFrame(PlayerHandle player, out OutboundFrame frame) =>
        TryGetFrame(player, static replica => replica.Health, out frame);

    public bool TryGetMovementFrame(PlayerHandle player, out OutboundFrame frame) =>
        TryGetFrame(player, static replica => replica.Movement, out frame);

    public bool TryGetItemFrame(PlayerHandle player, short slot, out OutboundFrame frame)
    {
        if (!TryGet(player, out ServerPlayerReplica replica) ||
            !replica.Items.TryGetValue(slot, out byte[]? encoded))
        {
            frame = default;
            return false;
        }

        frame = new OutboundFrame(encoded);
        return true;
    }

    public ServerPlayerBaselineEnqueueCounts EnqueueBaselines(RuntimeConnectionEndpoint recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        int active = 0;
        int appearance = 0;
        int equipment = 0;
        int health = 0;
        int mana = 0;
        int movement = 0;

        for (int slot = 0; slot < replicas.Length; slot++)
        {
            ServerPlayerReplica? replica = replicas[slot];
            if (replica is null)
                continue;

            active += TryEnqueue(recipient, replica.Active);
            appearance += TryEnqueue(recipient, replica.Appearance);
            foreach (byte[] item in replica.Items.Values)
                equipment += TryEnqueue(recipient, item);
            health += TryEnqueue(recipient, replica.Health);
            mana += TryEnqueue(recipient, replica.Mana);
            movement += TryEnqueue(recipient, replica.Movement);
        }

        return new ServerPlayerBaselineEnqueueCounts(active, appearance, equipment, health, mana, movement);
    }

    private bool TryGet(PlayerHandle player, out ServerPlayerReplica replica)
    {
        ServerPlayerReplica? current = player.IsAssigned
            ? replicas[player.Slot.Value]
            : null;
        if (current is null || current.Player != player)
        {
            replica = null!;
            return false;
        }

        replica = current;
        return true;
    }

    private bool TryGetFrame(
        PlayerHandle player,
        Func<ServerPlayerReplica, byte[]?> selector,
        out OutboundFrame frame)
    {
        if (!TryGet(player, out ServerPlayerReplica replica) ||
            selector(replica) is not byte[] encoded)
        {
            frame = default;
            return false;
        }

        frame = new OutboundFrame(encoded);
        return true;
    }

    private static int TryEnqueue(RuntimeConnectionEndpoint recipient, byte[]? encoded) =>
        encoded is not null &&
        recipient.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued
            ? 1
            : 0;

    private sealed class ServerPlayerReplica(PlayerHandle player, byte[] active)
    {
        public PlayerHandle Player { get; } = player;

        public byte[] Active { get; } = active ?? throw new ArgumentNullException(nameof(active));

        public byte[]? Appearance { get; set; }

        public SortedDictionary<short, byte[]> Items { get; } = [];

        public byte[]? Health { get; set; }

        public byte[]? Mana { get; set; }

        public byte[]? Movement { get; set; }
    }
}

internal readonly record struct ServerPlayerBaselineEnqueueCounts(
    int Active,
    int Appearance,
    int Equipment,
    int Health,
    int Mana,
    int Movement);
