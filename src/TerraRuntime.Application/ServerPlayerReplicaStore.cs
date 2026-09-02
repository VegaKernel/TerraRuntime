using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

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
        movement = EncodeMovement(in player);
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

        encoded = EncodeAppearance(player.Slot, in appearance);
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

        encoded = EncodeItem(player.Slot, in item);
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

        encoded = EncodeMovement(in player);
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

    private static byte[] EncodeAppearance(
        PlayerSlotId player,
        in ServerPlayerAppearanceState appearance)
    {
        var state = new TerrariaPlayerAppearanceState(
            player.Value,
            appearance.SkinVariant,
            appearance.VoiceVariant,
            appearance.VoicePitchOffset,
            appearance.Hair,
            appearance.Name,
            appearance.HairDye,
            appearance.HideVisibleAccessory,
            appearance.HideMisc,
            ToProtocol(appearance.HairColor),
            ToProtocol(appearance.SkinColor),
            ToProtocol(appearance.EyeColor),
            ToProtocol(appearance.ShirtColor),
            ToProtocol(appearance.UnderShirtColor),
            ToProtocol(appearance.PantsColor),
            ToProtocol(appearance.ShoeColor),
            appearance.DifficultyFlags,
            appearance.TorchAndCartFlags,
            appearance.ConsumableUnlockFlags);
        return TerrariaPlayerAppearanceCodec.Encode(in state);
    }

    private static byte[] EncodeItem(PlayerSlotId player, in ServerPlayerItemState item)
    {
        var state = new TerrariaPlayerEquipmentState(
            player.Value,
            item.Slot,
            item.Stack,
            checked((byte)item.Prefix.Value),
            checked((short)item.ItemType.Value),
            item.ItemFlags);
        return TerrariaPlayerEquipmentCodec.Encode(in state);
    }

    private static byte[] EncodeMovement(in PlayerStateSnapshot player)
    {
        var state = new TerrariaPlayerMovementState(
            player.Player.Slot.Value,
            player.ControlFlags,
            player.MovementFlags,
            player.MiscFlags1,
            player.MiscFlags2,
            player.SelectedItem,
            player.PositionX,
            player.PositionY,
            HasVelocity: true,
            player.VelocityX,
            player.VelocityY,
            HasMount: player.MountType != 0,
            player.MountType,
            HasPotionOfReturnPositions: false,
            player.PotionOfReturnOriginalPositionX,
            player.PotionOfReturnOriginalPositionY,
            player.PotionOfReturnHomePositionX,
            player.PotionOfReturnHomePositionY,
            HasCameraTarget: false,
            player.CameraTargetX,
            player.CameraTargetY);
        return TerrariaPlayerMovementEncoder.Encode(in state);
    }

    private static TerrariaRgbColor ToProtocol(PlayerRgbColor color) => new(color.R, color.G, color.B);

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
