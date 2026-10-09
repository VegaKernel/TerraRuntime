using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

/// <summary>
/// Owns the active client-player membership and pre-spawn vitals for one world runtime.
/// All mutation remains on the world's authoritative thread; connection generations prevent
/// stale commands from observing or removing a replacement player in the same slot.
/// </summary>
internal sealed class RuntimePlayerMembership
{
    private readonly Dictionary<byte, RuntimePlayerMember> _members = [];
    internal ulong Serial { get; private set; } = 1;
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
    internal int Count => _members.Count;

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
        if (Serial == ulong.MaxValue)
            throw new InvalidOperationException("Player membership serial exhausted.");
        if (!_members.TryAdd(slot, member))
            throw new InvalidOperationException($"Player slot {slot} already belongs to an active world member.");
        Serial++;
        _talkNpcSlots[slot] = TerrariaNpcTalkCodec.NoNpc;
        _townShopSessions[slot] = null;
    }

    public bool TryRemove(ConnectionHandle connection, out RuntimePlayerMember member)
    {
        member = null!;
        if (Serial == ulong.MaxValue || !TryGet(connection, out member))
            return false;

        byte slot = connection.Player.Slot.Value;
        if (!_members.Remove(slot))
            return false;
        Serial++;

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
    public byte Team { get; set; }
    public bool Hostile { get; set; }
    public bool GodMode { get; set; }
    public bool HasHealth { get; set; }
    public short Life { get; set; }
    public short MaxLife { get; set; }
    // Player constructor initializes statLifeMax2 independently from synchronized base vitals.
    public int? DerivedLifeMax { get; set; } = 100;
    public int? BaseLifeMax { get; set; } = 100;
    public bool? NpcLifeCurrent { get; set; } = false;
    public PlayerNpcHealthState1458? NpcHealth { get; set; } = PlayerNpcHealthState1458.Constructor;
    public int? NpcLife => Revision < ulong.MaxValue && NpcLifeCurrent == true
        ? NpcHealth?.Life ?? (HasHealth ? Life : null)
        : null;
    public PlayerDebuffSnapshot1458? Debuffs { get; set; } = default(PlayerDebuffSnapshot1458);
    public bool IsDead { get; set; }
    public float Stealth { get; set; } = 1f;
    // Player constructor owns clear zone bytes; imports deliberately overwrite this with nullable provenance.
    public PlayerZoneSnapshot1458? Zones { get; set; } = default(PlayerZoneSnapshot1458);
    public float Luck { get; set; }
    public VanillaPlayerLuckComponents1458? LuckComponents { get; set; }
    public int ItemAnimation { get; set; }
    public float ItemRotation { get; set; }
    public bool HasMana { get; set; }
    public short Mana { get; set; }
    public short MaxMana { get; set; }
    internal RuntimePlayerItemPhase1458? ItemPhase { get; set; } = RuntimePlayerItemPhase1458.Constructor;
    internal RuntimePlayerPhysicsPhase1458? PhysicsPhase { get; set; } = RuntimePlayerPhysicsPhase1458.Constructor;
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
    public bool HasMount { get; set; }
    public float PotionOfReturnOriginalPositionX { get; set; }
    public float PotionOfReturnOriginalPositionY { get; set; }
    public float PotionOfReturnHomePositionX { get; set; }
    public float PotionOfReturnHomePositionY { get; set; }
    public float CameraTargetX { get; set; }
    public float CameraTargetY { get; set; }

    public bool TryAdvanceRevision()
    {
        if (Revision == ulong.MaxValue || ProjectileUseInputRevision == ulong.MaxValue)
            return false;

        Revision++;
        ProjectileUseInputRevision++;
        ClearRemotePhaseSnapshot();
        return true;
    }

    // A pending packet27 volley owns its original launch event. Verified remote clock/motion
    // progress alone does not replace that input event, while commands and other writers do.
    internal ulong ProjectileUseInputRevision { get; private set; } = 1;
    internal PlayerStateSnapshot? RemotePhaseSnapshot { get; private set; }
    // A pose alone does not prove that the retained item clocks still belong to this phase.
    internal RuntimePlayerItemPhase1458? RemotePhaseItem { get; private set; }
    internal ulong RemotePhaseInputRevision { get; private set; }

    internal bool TryAdvanceRemotePhaseRevision()
    {
        if (Revision == ulong.MaxValue) return false;
        Revision++;
        return true;
    }

    internal void MarkRemotePhaseSnapshot()
    {
        RemotePhaseSnapshot = CaptureSnapshot();
        RemotePhaseItem = ItemPhase;
        RemotePhaseInputRevision = ProjectileUseInputRevision;
    }

    internal void ClearRemotePhaseSnapshot()
    {
        RemotePhaseSnapshot = null;
        RemotePhaseItem = null;
        RemotePhaseInputRevision = 0;
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
            Stealth = Stealth,
            Zones = Zones,
            Luck = Luck,
            LuckComponents = LuckComponents,
            ItemAnimation = ItemAnimation,
            ItemRotation = ItemRotation,
            HasMount = HasMount,
            Hostile = Hostile,
            HasHealth = HasHealth,
            Life = Life,
            MaxLife = MaxLife,
            DerivedLifeMax = DerivedLifeMax,
            BaseLifeMax = BaseLifeMax,
            NpcLifeCurrent = Revision < ulong.MaxValue ? NpcLifeCurrent : null,
            NpcHealth = Revision < ulong.MaxValue ? NpcHealth : null,
            Debuffs = Debuffs,
            IsDead = IsDead,
            HasMana = HasMana,
            Mana = Mana,
            MaxMana = MaxMana
        };
}
