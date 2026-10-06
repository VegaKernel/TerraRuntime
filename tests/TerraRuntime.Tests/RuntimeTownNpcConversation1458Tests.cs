using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using System.Reflection;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcConversation1458Tests
{
    [Theory]
    [InlineData(6f)]
    [InlineData(7f)]
    [InlineData(18f)]
    [InlineData(19f)]
    public void Player_poses_face_a_live_eligible_peer_and_commit_once(float pose)
    {
        var f = new Fixture(pose, 3f, 0f);
        f.Tick([Peer(0, -1, 600f, true)]);
        NpcSnapshot after = f.Current;
        Assert.Equal(pose, after.Ai.Ai0);
        Assert.Equal(2f, after.Ai.Ai1);
        Assert.Equal(23f, after.Ai.Ai3);
        Assert.Equal(pose == 18f ? 2f : 9f, after.Simulation.LocalAi.Ai3);
        Assert.Equal(-1, after.Simulation.DirectionX);
        Assert.Equal(.4f, after.VelocityX);
        Assert.Equal(0, f.Random.Calls);
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, Assert.Single(f.Sink.Commits));
        Assert.Equal(f.Initial.PositionX + .4f, after.PositionX);
    }

    [Theory]
    [InlineData(6f, 1f, 0f, true, 600f, false)]
    [InlineData(7f, 3f, 0f, false, 600f, false)]
    [InlineData(18f, 3f, 0f, true, 900f, false)]
    [InlineData(18f, 3f, 0f, true, 838.01f, false)]
    [InlineData(19f, 3f, 0f, true, 600f, true)]
    [InlineData(6f, 3f, -1f, true, 600f, false)]
    [InlineData(7f, 3f, 256f, true, 600f, false)]
    public void Expiry_ineligible_missing_far_or_obstructed_peer_exits_with_source_rng_order(
        float pose, float timer, float target, bool eligible, float x, bool obstruction)
    {
        var f = new Fixture(pose, timer, target);
        if (obstruction)
            f.Tiles.Set(39, 27, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Tick([Peer(0, -1, x, eligible)]);
        Assert.Equal(new NpcAiState(0f, 85f, 0f, 23f), f.Current.Ai);
        Assert.Equal(67f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(.4f, f.Current.VelocityX);
        Assert.Equal(2, f.Random.Calls);
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, Assert.Single(f.Sink.Commits));
    }

    [Theory]
    [InlineData(6f, -0.5f, 0, 638f, -1)]
    [InlineData(7f, 255f, 255, 838f, 1)]
    [InlineData(18f, 0f, 0, 438f, -1)]
    public void Source_index_truncation_equal_facing_and_inclusive_distance_200_are_preserved(
        float pose, float target, byte slot, float x, int direction)
    {
        var f = new Fixture(pose, 3f, target);
        f.Tick([Peer(slot, -1, x, true)]);
        Assert.Equal(pose, f.Current.Ai.Ai0);
        Assert.Equal(2f, f.Current.Ai.Ai1);
        Assert.Equal(direction, f.Current.Simulation.DirectionX);
        Assert.Equal(0, f.Random.Calls);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(1f, true)]
    [InlineData(5f, true)]
    [InlineData(6f, true)]
    [InlineData(7f, true)]
    [InlineData(18f, true)]
    [InlineData(19f, true)]
    public void Active_authenticated_talker_interrupts_before_pose_and_chair_checks_without_rng(
        float state, bool forced)
    {
        var f = new Fixture(state, 3f, 17f);
        // Eligibility and range do not gate source talkNPC interruption.
        f.Tick([Peer(0, 0, 1000f, false)]);
        Assert.Equal(new NpcAiState(0f, 299f, 17f, 23f), f.Current.Ai);
        Assert.Equal(99f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(1, f.Current.Simulation.DirectionX);
        Assert.Equal(.4f, f.Current.VelocityX);
        Assert.Equal(0, f.Random.Calls);
        Assert.Equal(forced ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update,
            Assert.Single(f.Sink.Commits));
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    [InlineData(14f)]
    [InlineData(15f)]
    [InlineData(24f)]
    public void Talking_cannot_replace_an_attack_or_special_pose(float state)
    {
        var f = new Fixture(state, 300f, 17f);
        f.Tick([Peer(0, 0, 600f, true)], night: true);
        if (state == 10f)
        {
            Assert.Equal(10f, f.Current.Ai.Ai0); Assert.Equal(299f, f.Current.Ai.Ai1);
            Assert.Equal(10f, f.Current.Simulation.LocalAi.Ai3);
            Assert.Equal(639.4f, f.Current.PositionX); Assert.Single(f.Sink.Commits);
        }
        else { Assert.Equal(f.Initial, f.Current); Assert.Empty(f.Sink.Commits); }
        Assert.Equal(0, f.Random.Calls);
    }

    [Fact]
    public void Last_source_slot_talker_wins_independent_of_membership_insertion_order_and_slot_255_is_excluded()
    {
        var f = new Fixture(5f, 300f, 17f);
        f.Tick([Peer(12, 0, 700f, true), Peer(1, 0, 600f, true), Peer(255, 0, 600f, true)]);
        Assert.Equal(1, f.Current.Simulation.DirectionX);
        Assert.Equal(299f, f.Current.Ai.Ai1);
        Assert.Single(f.Sink.Commits);
    }

    [Theory]
    [InlineData(0f, 2f)]
    [InlineData(.5f, 2f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(2f, 2f)]
    [InlineData(3f, 2f)]
    public void State_18_preserves_only_source_local_animation_choices(float local, float expected)
    {
        var f = new Fixture(18f, 3f, 0f);
        NpcSnapshot before = f.Current;
        var update = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai,
            before.Simulation with { LocalAi = before.Simulation.LocalAi with { Ai3 = local } });
        Assert.True(f.Npcs.TryUpdate(before.Handle, in update, out _));
        f.Sink.Commits.Clear();
        f.Tick([Peer(0, -1, 600f, true)]);
        Assert.Equal(expected, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(-1, f.Current.Simulation.DirectionY);
        Assert.Equal(0, f.Random.Calls);
        Assert.Single(f.Sink.Commits);
    }

    [Fact]
    public void Pose_with_unchanged_facing_is_not_forced_and_housing_loss_does_not_freeze_it()
    {
        var f = new Fixture(7f, 3f, 0f);
        Assert.True(f.Town.TryKickOut(0, out _));
        f.Tick([Peer(0, -1, 700f, true)], night: true);
        Assert.Equal(2f, f.Current.Ai.Ai1);
        Assert.Equal(NpcStateCommitKind.Update, Assert.Single(f.Sink.Commits));
    }

    [Fact]
    public void Authenticated_talk_ingress_reaches_the_next_town_phase_and_disconnect_removes_its_effect()
    {
        var f = new Fixture(5f, 300f, 17f);
        var players = new PlayerAuthority(null, f.Tiles);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = Session(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(2801), session.Handle);
        players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0)));
        var authority = new TownNpcAuthority(players, f.Npcs, new RuntimeProjectileStore(), f.Tiles,
            new RuntimeWorldProgressionMutations(), f.Town, null, null, null, null, false, false, null, false, false);
        authority.ApplyTalk(connection, 0, null);
        f.Sink.Commits.Clear();
        authority.TickLifecycle(null);
        Assert.Equal(299f, f.Current.Ai.Ai1);
        Assert.Equal(99f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(-1, f.Current.Simulation.DirectionX);
        Assert.Single(f.Sink.Commits);
        players.TryApply(new PlayerDisconnectRuntimeCommand(connection));
        f.Sink.Commits.Clear();
        authority.TickLifecycle(null);
        Assert.Equal(NpcStateCommitKind.Update, Assert.Single(f.Sink.Commits));
        Assert.Equal(298f, f.Current.Ai.Ai1); // The ordinary idle owner resumes instead of refreshing talk299.
        Assert.Equal(98f, f.Current.Simulation.LocalAi.Ai3);
    }

    [Theory]
    [InlineData(1f, false, false, false, true)]
    [InlineData(0f, false, false, false, false)]
    [InlineData(.5f, false, true, false, false)]
    [InlineData(1f, true, false, false, false)]
    [InlineData(1f, true, true, false, true)]
    [InlineData(1f, false, false, true, false)]
    public void Authoritative_visibility_uses_owned_stealth_buff_animation_and_death(
        float stealth, bool invisible, bool animation, bool dead, bool remains)
    {
        var f = new Fixture(18f, 300f, 0f);
        var players = new PlayerAuthority(null, f.Tiles);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = Session(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(2802), session.Handle);
        players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0)));
        Assert.True(players.TryGet(connection, out RuntimePlayerMember? member));
        member.PositionX = 600f;
        member.PositionY = 439f;
        member.IsDead = dead;
        member.MiscFlags2 = (byte)(1 << 6); // Item-use success must not masquerade as an animation.
        players.TryApply(new PlayerItemAnimationRuntimeCommand(connection, 0f, animation ? (short)1 : (short)0));
        players.TryApply(new PlayerStealthRuntimeCommand(connection, stealth));
        if (invisible)
            players.TryApply(new PlayerBuffTypesRuntimeCommand(connection,
                new PlayerBuffTypesCommitRequest(connection.Player.Slot, new[] { VanillaBuffIds.Invisibility })));
        var authority = new TownNpcAuthority(players, f.Npcs, new RuntimeProjectileStore(), f.Tiles,
            new RuntimeWorldProgressionMutations(), f.Town, null, null, null, null, false, false, null, false, false);
        f.Sink.Commits.Clear();
        authority.TickLifecycle(null);
        Assert.Equal(remains ? 18f : 0f, f.Current.Ai.Ai0);
        if (remains) Assert.Equal(299f, f.Current.Ai.Ai1);
        else Assert.InRange(f.Current.Ai.Ai1, 60f, 119f);
        Assert.Single(f.Sink.Commits);
    }

    [Fact]
    public void A_talk_command_from_another_connection_cannot_interrupt_the_live_pose()
    {
        var f = new Fixture(6f, 300f, 0f);
        var players = new PlayerAuthority(null, f.Tiles);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = Session(slots);
        var current = new ConnectionHandle(GameCommandSourceId.FromConnection(2803), session.Handle);
        players.TryApply(new PlayerSpawnRuntimeCommand(current, session,
            new PlayerSpawnCommitRequest(current.Player.Slot, 20, 20, 0, 0, 0, 0, 0)));
        Assert.True(players.TryGet(current, out RuntimePlayerMember? member));
        member.PositionX = 600f;
        member.PositionY = 439f;
        var authority = new TownNpcAuthority(players, f.Npcs, new RuntimeProjectileStore(), f.Tiles,
            new RuntimeWorldProgressionMutations(), f.Town, null, null, null, null, false, false, null, false, false);
        authority.ApplyTalk(new ConnectionHandle(GameCommandSourceId.FromConnection(2804), current.Player), 0, null);
        f.Sink.Commits.Clear();
        authority.TickLifecycle(null);
        Assert.Equal(6f, f.Current.Ai.Ai0);
        Assert.Equal(299f, f.Current.Ai.Ai1);
        Assert.Equal(9f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Single(f.Sink.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Alive_or_dead_respawn_closes_owned_talk_and_shop_without_an_invented_talk_packet(bool dead)
    {
        var f = new Fixture(5f, 300f, 17f);
        var state = new ServerRuntimeState(npcs: f.Npcs, worldTiles: f.Tiles, townNpcs: f.Town);
        var runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = Session(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(2805), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0)));
        state.Apply(new ClientNpcTalkRuntimeCommand(connection, new TerrariaNpcTalkState(connection.Player.Slot.Value, 0)));
        Assert.True(runtime.Players.TrySetTownShopSession(connection,
            new RuntimeTownShopSession1458(0, new NpcTypeId(17), RuntimeTownShopSessionKind1458.OrdinaryShop,
                1f, false, default, [], default)));
        if (dead)
            state.Apply(new PlayerHealthRuntimeCommand(connection,
                new PlayerHealthCommitRequest(connection.Player.Slot, 0, 100)));
        Assert.True(state.TryGetPlayerTalkNpc(connection.Player, out short before));
        Assert.Equal(0, before);
        state.Apply(new PlayerRespawnRuntimeCommand(connection,
            new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 1, 0, 0, 0)));
        Assert.True(state.TryGetPlayerTalkNpc(connection.Player, out short after));
        Assert.Equal(TerrariaNpcTalkCodec.NoNpc, after);
        Assert.False(runtime.Players.TryGetTownShopSession(connection.Player, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Owned_dead_phase_closes_talk_and_shop_before_the_npc_phase_even_without_an_animation(int animation)
    {
        var f = new Fixture(5f, 300f, 17f);
        var state = new ServerRuntimeState(npcs: f.Npcs, npcAiStepper: new NoNpcStep(),
            worldTiles: f.Tiles, townNpcs: f.Town);
        var runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = Session(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(2806), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0)));
        state.Apply(new ClientNpcTalkRuntimeCommand(connection, new TerrariaNpcTalkState(connection.Player.Slot.Value, 0)));
        Assert.True(runtime.Players.TrySetTownShopSession(connection,
            new RuntimeTownShopSession1458(0, new NpcTypeId(17), RuntimeTownShopSessionKind1458.OrdinaryShop,
                1f, false, default, [], default)));
        state.Apply(new PlayerItemAnimationRuntimeCommand(connection, 0f, checked((short)animation)));
        state.Apply(new PlayerHealthRuntimeCommand(connection,
            new PlayerHealthCommitRequest(connection.Player.Slot, 0, 100)));
        Assert.True(state.TryGetPlayerTalkNpc(connection.Player, out short before));
        Assert.Equal(0, before);

        state.Tick();

        Assert.True(state.TryGetPlayerTalkNpc(connection.Player, out short after));
        Assert.Equal(TerrariaNpcTalkCodec.NoNpc, after);
        Assert.False(runtime.Players.TryGetTownShopSession(connection.Player, out _));
        Assert.InRange(f.Current.Ai.Ai1, 60f, 119f); // Chair expiry ran; a stale talker would force 299.
        Assert.InRange(f.Current.Simulation.LocalAi.Ai3, 30f, 89f);
    }

    private sealed class NoNpcStep : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private static PlayerJoinSession Session(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static RuntimeTownPlayerConversation1458 Peer(byte slot, short talk, float x, bool eligible) =>
        new(slot, talk, new RuntimeTownPlayerBounds1458(x, 439f, 20f, 42f), eligible);

    private sealed class Fixture
    {
        public Fixture(float state, float timer, float target)
        {
            Tiles = new WorldTileStore(new WorldDimensions(100, 80));
            for (int x = 0; x < 100; x++) Tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [
                new WorldTownNpc(17, "Merchant", 639f, 440f, false, 40, 30, null, false) ], []),
                [new WorldTownRoom(17, 40, 30)], Tiles.Dimensions);
            Npcs = new RuntimeNpcStore(commitSink: Sink);
            Assert.True(Town.TryReserveRuntimeSlots(Npcs));
            Assert.True(Npcs.TryGetActive(0, out NpcSnapshot initial));
            var update = new NpcStateUpdate(initial.Type, initial.NetId, initial.PositionX, initial.PositionY,
                .5f, 0f, initial.Target, new NpcAiState(state, timer, target, 23f),
                initial.Simulation with { DirectionX = 1, LocalAi = new NpcAiState(0f, 0f, 0f, 9f) });
            Assert.True(Npcs.TryUpdate(initial.Handle, in update, out NpcSnapshot seeded));
            Initial = seeded;
            Schedule = new RuntimeTownNpcSchedule1458(Town, Npcs, Tiles, Random);
            Phase = new(Town, Npcs, Tiles, Schedule);
            Sink.Commits.Clear();
        }
        public WorldTileStore Tiles { get; }
        public RuntimeTownNpcStateStore Town { get; }
        public RuntimeNpcStore Npcs { get; }
        public NpcSnapshot Initial { get; }
        public NpcSnapshot Current { get { Assert.True(Npcs.TryGetActive(0, out NpcSnapshot current)); return current; } }
        public Sink Sink { get; } = new();
        public OrderedRandom Random { get; } = new();
        public RuntimeTownNpcSchedule1458 Schedule { get; }
        public TownNpcTestPhase1458 Phase { get; }
        public void Tick(RuntimeTownPlayerConversation1458[] peers, bool night = false)
        {
            var conditions = new RuntimeTownNpcScheduleConditions1458(!night, false, false, false, false);
            Phase.Tick(in conditions, [], peers);
        }
    }

    private sealed class OrderedRandom : IRuntimeTownNpcScheduleRandom1458
    {
        public int Calls { get; private set; }
        public int Next(int maximum) => maximum == 60 ? (Calls++ == 0 ? 25 : 37) : maximum - 1;
    }
    private sealed class Sink : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Commits.Add(kind);
    }
}
