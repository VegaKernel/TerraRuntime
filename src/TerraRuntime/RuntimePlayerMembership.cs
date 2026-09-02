using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime;

/// <summary>
/// Owns the active client-player membership and pre-spawn vitals for one world runtime.
/// All mutation remains on the world's authoritative thread; connection generations prevent
/// stale commands from observing or removing a replacement player in the same slot.
/// </summary>
internal sealed class RuntimePlayerMembership
{
    private readonly Dictionary<byte, RuntimePlayerMember> _members = [];
    private readonly RuntimePendingPlayerVitals?[] _pendingVitals;
    private readonly short[] _talkNpcSlots;
    private readonly RuntimeTownShopSession1458?[] _townShopSessions;

    public RuntimePlayerMembership(int capacity)
    {
        if (capacity is <= 0 or > byte.MaxValue + 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _pendingVitals = new RuntimePendingPlayerVitals?[capacity];
        _talkNpcSlots = new short[capacity];
        _townShopSessions = new RuntimeTownShopSession1458?[capacity];
        Array.Fill(_talkNpcSlots, TerrariaNpcTalkCodec.NoNpc);
    }

    public IEnumerable<RuntimePlayerMember> Members => _members.Values;

    public bool Contains(PlayerSlotId slot) => _members.ContainsKey(slot.Value);

    public bool IsCurrent(ConnectionHandle connection) =>
        connection.IsAssigned &&
        TryGet(connection, out _);

    public bool TryGet(ConnectionHandle connection, out RuntimePlayerMember member)
    {
        if (connection.IsAssigned &&
            _members.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerMember? current) &&
            current.Connection == connection)
        {
            member = current;
            return true;
        }

        member = null!;
        return false;
    }

    public bool TryGet(PlayerHandle player, out RuntimePlayerMember member)
    {
        if (player.IsAssigned &&
            _members.TryGetValue(player.Slot.Value, out RuntimePlayerMember? current) &&
            current.Connection.Player == player)
        {
            member = current;
            return true;
        }

        member = null!;
        return false;
    }

    public bool TryGet(PlayerSlotId slot, out RuntimePlayerMember member) =>
        TryGet(slot.Value, out member);

    public bool TryGet(byte slot, out RuntimePlayerMember member) =>
        _members.TryGetValue(slot, out member!);

    public void Commit(RuntimePlayerMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (!member.Connection.IsAssigned || member.Connection.Player.Slot != member.Slot)
            throw new ArgumentException("Player membership must match its assigned connection slot.", nameof(member));

        byte slot = member.Slot.Value;
        if (!_members.TryAdd(slot, member))
            throw new InvalidOperationException($"Player slot {slot} already belongs to an active world member.");
        _talkNpcSlots[slot] = TerrariaNpcTalkCodec.NoNpc;
        _townShopSessions[slot] = null;
    }

    public bool TryRemove(ConnectionHandle connection, out RuntimePlayerMember member)
    {
        if (!TryGet(connection, out member))
            return false;

        byte slot = connection.Player.Slot.Value;
        if (!_members.Remove(slot))
            return false;

        _talkNpcSlots[slot] = TerrariaNpcTalkCodec.NoNpc;
        _townShopSessions[slot] = null;
        return true;
    }

    public bool TrySetTalkNpc(ConnectionHandle connection, short npcSlot)
    {
        if (!TerrariaNpcTalkCodec.IsValidNpcSlot(npcSlot) || !TryGet(connection, out _))
            return false;

        byte slot = connection.Player.Slot.Value;
        _talkNpcSlots[slot] = npcSlot;
        _townShopSessions[slot] = null;
        return true;
    }

    public bool TryGetTalkNpc(PlayerHandle player, out short npcSlot)
    {
        if (!TryGet(player, out _))
        {
            npcSlot = TerrariaNpcTalkCodec.NoNpc;
            return false;
        }

        npcSlot = _talkNpcSlots[player.Slot.Value];
        return true;
    }

    public bool TrySetTownShopSession(ConnectionHandle connection, RuntimeTownShopSession1458 session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!TryGet(connection, out _) ||
            _talkNpcSlots[connection.Player.Slot.Value] != session.NpcSlot)
            return false;

        _townShopSessions[connection.Player.Slot.Value] = session;
        return true;
    }

    public bool TryGetTownShopSession(PlayerHandle player, out RuntimeTownShopSession1458? session)
    {
        if (!TryGet(player, out _) ||
            _townShopSessions[player.Slot.Value] is not RuntimeTownShopSession1458 current)
        {
            session = null;
            return false;
        }

        session = current;
        return true;
    }

    public RuntimePendingPlayerVitals GetOrReplacePending(ConnectionHandle connection)
    {
        if (!connection.IsAssigned)
            throw new ArgumentException("Pending player vitals require an assigned connection.", nameof(connection));

        int slot = connection.Player.Slot.Value;
        RuntimePendingPlayerVitals? pending = _pendingVitals[slot];
        if (pending is null || pending.Connection != connection)
        {
            pending = new RuntimePendingPlayerVitals(connection);
            _pendingVitals[slot] = pending;
        }

        return pending;
    }

    public RuntimePendingPlayerVitals? TakePending(PlayerSlotId slot)
    {
        RuntimePendingPlayerVitals? pending = _pendingVitals[slot.Value];
        _pendingVitals[slot.Value] = null;
        return pending;
    }

    public void ClearPending(ConnectionHandle connection)
    {
        int slot = connection.Player.Slot.Value;
        RuntimePendingPlayerVitals? pending = _pendingVitals[slot];
        if (pending is not null && pending.Connection == connection)
            _pendingVitals[slot] = null;
    }

    public bool TryCapture(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (!TryGet(player, out RuntimePlayerMember member))
        {
            snapshot = default;
            return false;
        }

        snapshot = member.CaptureSnapshot();
        return true;
    }
}

internal sealed class RuntimePendingPlayerVitals(ConnectionHandle connection)
{
    public ConnectionHandle Connection { get; } = connection;
    public bool HasHealth { get; set; }
    public short Life { get; set; }
    public short MaxLife { get; set; }
    public bool HasMana { get; set; }
    public short Mana { get; set; }
    public short MaxMana { get; set; }
}

internal sealed class RuntimePlayerMember
{
    public ConnectionHandle Connection { get; init; }
    public ulong Revision { get; set; }
    public PlayerSlotId Slot { get; init; }
    public byte Team { get; init; }
    public bool HasHealth { get; set; }
    public short Life { get; set; }
    public short MaxLife { get; set; }
    public bool IsDead { get; set; }
    public bool HasMana { get; set; }
    public short Mana { get; set; }
    public short MaxMana { get; set; }
    public byte ControlFlags { get; set; }
    public byte MovementFlags { get; set; }
    public byte MiscFlags1 { get; set; }
    public byte MiscFlags2 { get; set; }
    public byte SelectedItem { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public ushort MountType { get; set; }
    public float PotionOfReturnOriginalPositionX { get; set; }
    public float PotionOfReturnOriginalPositionY { get; set; }
    public float PotionOfReturnHomePositionX { get; set; }
    public float PotionOfReturnHomePositionY { get; set; }
    public float CameraTargetX { get; set; }
    public float CameraTargetY { get; set; }

    public bool TryAdvanceRevision()
    {
        if (Revision == ulong.MaxValue)
            return false;

        Revision++;
        return true;
    }

    public PlayerStateSnapshot CaptureSnapshot() =>
        new(
            Connection.Player,
            new PlayerStateRevision(Revision),
            Team,
            ControlFlags,
            MovementFlags,
            MiscFlags1,
            MiscFlags2,
            SelectedItem,
            PositionX,
            PositionY,
            VelocityX,
            VelocityY,
            MountType,
            PotionOfReturnOriginalPositionX,
            PotionOfReturnOriginalPositionY,
            PotionOfReturnHomePositionX,
            PotionOfReturnHomePositionY,
            CameraTargetX,
            CameraTargetY)
        {
            HasHealth = HasHealth,
            Life = Life,
            MaxLife = MaxLife,
            IsDead = IsDead,
            HasMana = HasMana,
            Mana = Mana,
            MaxMana = MaxMana
        };
}
