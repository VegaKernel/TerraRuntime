using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RemoteItemPhase1458Tests
{
    [Fact]
    public void Actual_ascending_UpdatePlayers_census_matches_remote_vitals_timers_physics_buffs_and_shared_cursor()
    {
        using var source = Read();
        Assert.Equal(8, source.RootElement.GetArrayLength());
        foreach (bool runtimeTick in new[] { false, true })
        foreach (var row in source.RootElement.EnumerateArray())
        {
            string mode = row.GetProperty("mode").GetString()!;
            using var f = new Fixture(mode, runtimeTick);
            var sentinel = f.Member(255).CaptureSnapshot();
            var sentinelPhase = f.Member(255).ItemPhase;
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                int tick = I(step, "tick");
                foreach (byte slot in new byte[] { 0, 2 })
                    if (f.Players.TryGet(slot, out _)) f.Move(slot, tick > 0);
                f.Events.Count = 0;
                Assert.True(f.Step(), $"{mode}/{tick}/runtime={runtimeTick}");
                Assert.Equal(0, f.Events.Count);
                AssertRandom(step.GetProperty("after"), f.Random);
                foreach (var expected in step.GetProperty("players").EnumerateArray())
                {
                    byte slot = checked((byte)I(expected, "slot"));
                    if (slot == 255 || mode == "inactive-first" && slot == 0) continue;
                    var member = f.Member(slot);
                    Assert.Equal(F(expected, "position", "X"), member.PositionX);
                    Assert.Equal(F(expected, "position", "Y"), member.PositionY);
                    Assert.Equal(F(expected, "velocity", "X"), member.VelocityX);
                    Assert.Equal(F(expected, "velocity", "Y"), member.VelocityY);
                    Assert.Equal(I(expected, "life"), member.CaptureSnapshot().NpcLife);
                    Assert.Equal(I(expected, "lifeMaximum"), member.DerivedLifeMax);
                    Assert.Equal(I(expected, "mana"), member.Mana);
                    Assert.Equal(50, member.Life); // Packet16 observation is distinct from projected source life.
                    Assert.Equal(200, member.MaxMana);
                    var phase = member.ItemPhase!.Value;
                    Assert.Equal(I(expected, "maximum"), phase.Mana.Maximum);
                    Assert.Equal(I(expected, "count"), phase.Mana.Count);
                    Assert.Equal(expected.GetProperty("delay").GetSingle(), phase.Mana.Delay);
                    Assert.Equal(expected.GetProperty("heat").GetSingle(), phase.ManaHeat);
                    Assert.Equal(I(expected, "animation"), phase.Selected.Animation);
                    Assert.Equal(I(expected, "animationMax"), phase.Selected.AnimationMax);
                    Assert.Equal(I(expected, "itemTime"), phase.Selected.ItemTime);
                    Assert.Equal(I(expected, "itemTimeMax"), phase.Selected.ItemTimeMax);
                    Assert.Equal(expected.GetProperty("release").GetBoolean(), phase.Selected.ReleaseUseItem);
                    Assert.Equal(I(expected, "crit"), phase.Selected.RevolverCritBonus);
                    f.Buffs(slot).CaptureSlots(out var types, out var times);
                    Assert.Equal(expected.GetProperty("buffTypes").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
                    Assert.Equal(expected.GetProperty("buffTimes").EnumerateArray().Select(x => x.GetInt32()), times);
                    Assert.True(f.Players.TryGetInventoryItem(f.Connections[slot], 0, out var item));
                    Assert.Equal(f.ItemIds[slot] == 0 ? 0 : 3, item.Stack);
                }
                Assert.Equal(sentinel, f.Member(255).CaptureSnapshot());
                Assert.Equal(sentinelPhase, f.Member(255).ItemPhase);
            }
        }
    }

    [Fact]
    public void Late_health_callbacks_refuse_the_entire_census_before_any_planned_random_status_or_motion_adoption()
    {
        foreach (string mutation in new[] { "inventory", "player", "tile", "disconnect" })
        {
            using var f = new Fixture("empty-first");
            var item0 = f.Member(0).ItemPhase;
            var item2 = f.Member(2).ItemPhase;
            var physical0 = f.Member(0).PhysicsPhase;
            var physical2 = f.Member(2).PhysicsPhase;
            var before0 = f.Member(0).CaptureSnapshot();
            var random = f.Random.Clone();
            bool called = false;
            f.Players.SetNpcHealthWorldFacts(() =>
            {
                if (!called)
                {
                    called = true;
                    if (mutation == "inventory")
                        f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Connections[2], new(new(2), 0, 4, 0, 28, 0)));
                    else if (mutation == "player") f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connections[2], new(new(2), 49, 400)));
                    else if (mutation == "tile") f.Tiles.Set(110, 103, new WorldTile { Type = 0, Flags = WorldTileFlags.Active });
                    else f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connections[2]));
                }
                return new(false, false);
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase(), mutation);
            Assert.True(called, mutation);
            Assert.True(f.Random.HasSameState(random));
            Assert.Equal(before0, f.Member(0).CaptureSnapshot());
            Assert.Equal(item0, f.Member(0).ItemPhase);
            Assert.Equal(physical0, f.Member(0).PhysicsPhase);
            if (mutation != "disconnect")
            {
                Assert.Equal(item2, f.Member(2).ItemPhase);
                Assert.Equal(physical2, f.Member(2).PhysicsPhase);
                Assert.False(f.Buffs(2).Contains(VanillaBuffIds.PotionSickness));
            }
        }
    }

    [Fact]
    public void Actual_nonconstructor_early_returns_preserve_or_age_only_the_source_selected_clocks_and_maxima()
    {
        using var stream = typeof(RemoteItemPhase1458Tests).Assembly.GetManifestResourceStream("PlayerRemoteEarlyReturns1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        Assert.Equal(5, source.RootElement.GetProperty("rows").GetArrayLength());
        int compared = 0;
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            string mode = row.GetProperty("mode").GetString()!;
            // The two separate mana boundary profiles remain original evidence for their component.
            if (mode is not ("dead-first" or "ghost-first" or "outside-first")) continue;
            using var f = new Fixture(mode);
            f.ImportPhase(0, row.GetProperty("initial"));
            f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connections[2]));
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                f.Events.Count = 0;
                Assert.True(f.Players.TryTickRemotePlayerPhase(), mode);
                Assert.Equal(0, f.Events.Count);
                AssertRandom(step.GetProperty("after"), f.Random);
                var expected = step.GetProperty("players")[0];
                var member = f.Member(0);
                var item = member.ItemPhase!.Value;
                Assert.Equal(I(expected, "life"), member.CaptureSnapshot().NpcLife);
                Assert.Equal(I(expected, "lifeMax"), member.DerivedLifeMax);
                Assert.Equal(I(expected, "lifeCount"), member.NpcHealth!.Value.RegenCount);
                Assert.Equal(expected.GetProperty("lifeTime").GetSingle(), member.NpcHealth.Value.RegenTime);
                Assert.Equal(I(expected, "mana"), member.Mana);
                Assert.Equal(I(expected, "maximum"), item.Mana.Maximum);
                Assert.Equal(I(expected, "count"), item.Mana.Count);
                Assert.Equal(expected.GetProperty("delay").GetSingle(), item.Mana.Delay);
                Assert.Equal(expected.GetProperty("heat").GetSingle(), item.ManaHeat);
                Assert.Equal(I(expected, "animation"), item.Selected.Animation);
                Assert.Equal(I(expected, "animationMax"), item.Selected.AnimationMax);
                Assert.Equal(I(expected, "itemTime"), item.Selected.ItemTime);
                Assert.Equal(I(expected, "itemTimeMax"), item.Selected.ItemTimeMax);
                Assert.Equal(expected.GetProperty("release").GetBoolean(), item.Selected.ReleaseUseItem);
                Assert.Equal(I(expected, "potionDelay"), item.Selected.PotionDelay);
                Assert.Equal(I(expected, "manaPotionDelay"), item.ManaPotionDelay);
                Assert.Equal(I(expected, "deadTime"), item.DeadTime);
                Assert.Equal(I(expected, "respawnTimer"), item.RespawnTimer);
                Assert.Equal(I(expected, "jump"), member.PhysicsPhase!.Value.Jump.RemainingTicks);
                Assert.Equal(expected.GetProperty("releaseJump").GetBoolean(), member.PhysicsPhase.Value.Jump.ReleaseReady);
                f.Buffs(0).CaptureSlots(out var types, out var times);
                Assert.Equal(expected.GetProperty("buffTypes").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
                Assert.Equal(expected.GetProperty("buffTimes").EnumerateArray().Select(x => x.GetInt32()), times);
                compared++;
            }
        }
        Assert.Equal(12, compared);
    }

    [Fact]
    public void Actual_runtime_tick_retires_unknown_world_cursor_and_reports_reconnect_or_same_binding_cannot_recover_it()
    {
        using var f = new Fixture("empty-first", runtimeTick: true);
        // Iron Pickaxe is outside this source-owned remote clock lane; admitted gun98 is not.
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connections[0], new(new(0), 0, 1, 0, 1, 0)));
        var sourceRandom = f.Random.Clone();
        Assert.False(f.Step());
        foreach (byte slot in new byte[] { 0, 2 })
        {
            Assert.Null(f.Member(slot).ItemPhase);
            Assert.Null(f.Member(slot).PhysicsPhase);
        }
        Assert.True(f.Random.HasSameState(sourceRandom));
        Assert.Equal(50, f.Member(2).CaptureSnapshot().NpcLife);
        Assert.False(f.Buffs(2).Contains(VanillaBuffIds.PotionSickness));
        foreach (byte slot in new byte[] { 0, 2 })
        {
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connections[slot], new(new(slot), 0, 0, 0, 0, 0)));
            f.Apply(new PlayerManaRuntimeCommand(f.Connections[slot], new(new(slot), 10, 200)));
            f.Apply(new PlayerBuffTypesRuntimeCommand(f.Connections[slot], new(new(slot), Array.Empty<BuffTypeId>())));
        }
        f.Players.SetPlayerUpdateRandom(f.Random);
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.True(f.Random.HasSameState(sourceRandom));
        f.ReconnectFirst();
        Assert.False(f.Step());
        Assert.Null(f.Member(0).ItemPhase);
        Assert.Null(f.Member(2).ItemPhase);
        Assert.True(f.Random.HasSameState(sourceRandom));
        Assert.Throws<InvalidOperationException>(() => f.Players.SetPlayerUpdateRandom(new(0)));
    }

    [Fact]
    public void Empty_actual_runtime_keeps_the_source_UpdatePlayers_seed_owned_before_any_join_even_without_world_facts()
    {
        var state = new ServerRuntimeState(playerUpdateRandomSeed: new(0));
        var players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(state)!).Players;
        var random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(players)!;
        var before = random.Clone();
        for (int i = 0; i < 4; i++)
        {
            state.Tick();
            Assert.True(players.TryTickRemotePlayerPhase());
            Assert.True(random.HasSameState(before));
        }
        players.SetPlayerUpdateRandom(random);
        Assert.True(players.TryTickRemotePlayerPhase());
        Assert.True(random.HasSameState(before));
    }

    [Fact]
    public void Actual_packet13_dual_horizontal_controls_follow_source_velocity_branch_in_the_runtime_tick()
    {
        using var stream = typeof(RemoteItemPhase1458Tests).Assembly.GetManifestResourceStream("PlayerDualHorizontal1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var rows = source.RootElement.GetProperty("reverse");
        Assert.Equal(5, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            using var f = new Fixture("empty-first", runtimeTick: true);
            f.Apply(new PlayerDisconnectRuntimeCommand(f.Connections[2]));
            var before = row.GetProperty("before");
            f.Apply(new PlayerMovementRuntimeCommand(f.Connections[0], new(new(0), 76, 20, 0, 64, 0,
                F(before, "position", "X"), F(before, "position", "Y"), true,
                row.GetProperty("inputVx").GetSingle(), 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.Equal(row.GetProperty("inputVx").GetSingle(), f.Member(0).VelocityX);
            f.Events.Count = 0;
            f.Tick();
            var after = row.GetProperty("after");
            var member = f.Member(0);
            Assert.Equal(F(after, "position", "X"), member.PositionX);
            Assert.Equal(F(after, "position", "Y"), member.PositionY);
            Assert.Equal(F(after, "velocity", "X"), member.VelocityX);
            Assert.Equal(F(after, "velocity", "Y"), member.VelocityY);
            Assert.Equal(after.GetProperty("heat").GetSingle(), member.ItemPhase!.Value.ManaHeat);
            Assert.Equal(after.GetProperty("releaseJump").GetBoolean(), member.PhysicsPhase!.Value.Jump.ReleaseReady);
            Assert.Equal(after.GetProperty("wingTime").GetSingle(), member.PhysicsPhase.Value.Jump.WingTime);
            Assert.Equal(row.GetProperty("cursor").GetUInt32(), typeof(VanillaUnifiedRandom1458)
                .GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Random));
            Assert.Equal(0, f.Events.Count);
        }
        var earlyRows = source.RootElement.GetProperty("earlyWing");
        Assert.Equal(3, earlyRows.GetArrayLength());
        foreach (var row in earlyRows.EnumerateArray())
        {
            using var f = new Fixture(row.GetProperty("mode").GetString()!, runtimeTick: true);
            f.Apply(new PlayerDisconnectRuntimeCommand(f.Connections[2]));
            f.ImportPhase(0, row.GetProperty("initial"));
            Assert.Equal(row.GetProperty("initial").GetProperty("wingTime").GetSingle(), f.Member(0).PhysicsPhase!.Value.Jump.WingTime);
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                f.Tick();
                var expected = step.GetProperty("players")[0];
                Assert.Equal(expected.GetProperty("wingTime").GetSingle(), f.Member(0).PhysicsPhase!.Value.Jump.WingTime);
                AssertRandom(step.GetProperty("after"), f.Random);
            }
        }
    }

    [Fact]
    public void Unknown_selected_actor_and_unknown_retained_phase_do_not_advance_later_players_or_the_world_stream()
    {
        foreach (bool unknownItem in new[] { true, false })
        {
            using var f = new Fixture("empty-first");
            if (unknownItem) f.Apply(new PlayerEquipmentRuntimeCommand(f.Connections[0], new(new(0), 0, 1, 0, 1, 0)));
            else f.Member(0).ItemPhase = null;
            var before = f.Member(2).CaptureSnapshot();
            var phase = f.Member(2).ItemPhase;
            var random = f.Random.Clone();
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(before, f.Member(2).CaptureSnapshot());
            Assert.Equal(phase, f.Member(2).ItemPhase);
            Assert.True(f.Random.HasSameState(random));
            Assert.False(f.Buffs(2).Contains(VanillaBuffIds.PotionSickness));
        }
    }

    private static JsonDocument Read()
    {
        using var stream = typeof(RemoteItemPhase1458Tests).Assembly.GetManifestResourceStream("RemoteItemPhaseCensus1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static int I(JsonElement x, string field) => x.GetProperty(field).GetInt32();
    private static float F(JsonElement x, string field, string component) => x.GetProperty(field).GetProperty(component).GetSingle();
    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly EventSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal Fixture(string mode, bool runtimeTick = false)
        {
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore();
            if (runtimeTick)
            {
                state = new(playerEvents: Events, worldTiles: Tiles, projectiles: projectiles,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                    playerUpdateRandomSeed: new(0));
                Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(state)!).Players;
                Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            }
            else { Players = new(Events, Tiles); Random = new(0); }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetPlayerUpdateRandom(Random);
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }
            var membership = (RuntimePlayerMembership)typeof(PlayerAuthority).GetField("membership", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            // Slot255 is not a connection lease. A trusted imported source placeholder remains untouched.
            membership.Commit(new RuntimePlayerMember { Slot = new(255), Revision = 1, Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))), ItemPhase = null, PhysicsPhase = null });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode == "inactive-first") continue;
                var session = sessions[slot]; session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (short)28 : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
                if (slot == 2 && mode == "potion-first") id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first") { Member(slot).PositionX = 0; Member(slot).PositionY = 0; }
                if (slot == 0 && mode == "dead-first") Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first") Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot) { Assert.True(Players.TryGet(slot, out var member)); return member; }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null) Assert.True(Players.TryApply(command));
            else state.Apply(command);
        }
        internal void Tick() { Assert.NotNull(state); state.Tick(); }
        internal bool Step()
        {
            if (state is null) return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99100), session.Handle);
            Connections[0] = connection;
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Move(byte slot, bool control)
        {
            var member = Member(slot);
            bool first = moved.Add(slot);
            if (!first && member.PositionX == 0 && member.PositionY == 0)
            {
                // Trusted source-configured outside import: ordinary movement would reject this geometry.
                member.ControlFlags = control ? (byte)32 : (byte)0;
                return;
            }
            Apply(new PlayerMovementRuntimeCommand(Connections[slot], new(new(slot), control ? (byte)32 : (byte)0,
                (byte)(member.MovementFlags | 16), 0, 64, 0, first ? 1600 + slot * 40 : member.PositionX,
                first ? 1606 : member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal PlayerBuffState Buffs(byte slot) => ((RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
            .GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!).CaptureBuffState(Connections[slot])!;
        internal void ImportPhase(byte slot, JsonElement source)
        {
            var member = Member(slot);
            member.PositionX = F(source, "position", "X"); member.PositionY = F(source, "position", "Y");
            member.VelocityX = F(source, "velocity", "X"); member.VelocityY = F(source, "velocity", "Y");
            member.ControlFlags = source.TryGetProperty("control", out var control) && !control.GetBoolean() ? (byte)0 : (byte)32;
            member.ItemAnimation = I(source, "animation");
            member.DerivedLifeMax = I(source, "lifeMax");
            member.NpcHealth = member.NpcHealth!.Value with { Life = I(source, "life"), RegenCount = I(source, "lifeCount"), RegenTime = source.GetProperty("lifeTime").GetSingle() };
            member.ItemPhase = member.ItemPhase!.Value with
            {
                Mana = new(I(source, "mana"), I(source, "maximum"), source.GetProperty("delay").GetSingle(), I(source, "count"), 0),
                ManaHeat = source.GetProperty("heat").GetSingle(),
                Selected = new(I(source, "itemTime"), I(source, "itemTimeMax"), I(source, "animation"), I(source, "animationMax"),
                    source.GetProperty("release").GetBoolean(), I(source, "potionDelay"), I(source, source.TryGetProperty("crit", out _) ? "crit" : "revolver")),
                DeadTime = source.TryGetProperty("deadTime", out var deadTime) ? deadTime.GetInt32() : 0,
                RespawnTimer = source.TryGetProperty("respawnTimer", out var respawn) ? respawn.GetInt32() : 0,
                ManaPotionDelay = source.TryGetProperty("manaPotionDelay", out var manaDelay) ? manaDelay.GetInt32() : 0
            };
            member.PhysicsPhase = member.PhysicsPhase!.Value with { Jump = new(I(source, "jump"), source.GetProperty("releaseJump").GetBoolean(), source.TryGetProperty("wingTime", out var wingTime) ? wingTime.GetInt32() : 0) };
            var profiles = (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority).GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            var before = profiles.CaptureBuffState(Connections[slot])!;
            var imported = PlayerBuffState.FromSlots(source.GetProperty("buffTypes").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray(),
                source.GetProperty("buffTimes").EnumerateArray().Select(x => x.GetInt32()).ToArray());
            Assert.True(profiles.TryAdoptBuffState(Connections[slot], before, imported));
        }
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) => Count++;
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) => Count++;
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) => Count++;
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) => Count++;
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
