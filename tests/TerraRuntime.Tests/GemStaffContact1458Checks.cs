using Xunit;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Players;

using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.GemStaffContacts;

internal static class GemStaffContact1458Checks
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "npcs")]
    private static extern ref RuntimeNpcStore CaptureNpcs(NpcAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "projectiles")]
    private static extern ref RuntimeProjectileStore CaptureProjectiles(ProjectileAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "sourceRandom")]
    private static extern ref VanillaUnifiedRandom1458 GemRandom(RuntimeProjectileNpcCombatPass owner);
    internal static void GemCase(JsonElement row, bool noItems = false)
    {
        using var f = new GemContactFixture("empty-selected", runtimeTick: true);
        var c = f.Connections[2];
        var sourceOwner = row.GetProperty("beforeOwner");
        var bCombat = VanillaPlayerCombatSnapshot.Baseline with { MagicCrit = sourceOwner.GetProperty("magicCrit").GetInt32(), ArmorPenetration = sourceOwner.GetProperty("armorPenetration").GetInt32(), MagicDamage = sourceOwner.GetProperty("magicDamage").GetSingle() };
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c)); f.Tick();
        var m = f.Member(2);
        var composition = Composition(f.Runtime);
        var shots = CaptureProjectiles(composition.Projectiles);
        var features = (TerraRuntime.Gameplay.Projectiles.GemStaffFeatures1458)row.GetProperty("mask").GetInt32();
        m.ItemPhase = m.ItemPhase!.Value with { DerivedCombat = bCombat };
        Assert.True(Profiles(f.Players).TrySetBuffTypes(c, sourceOwner.GetProperty("buffType").EnumerateArray().Where(x => x.GetInt32() > 0).Select(x => new BuffTypeId((ushort)x.GetInt32())).ToArray()));
        var update = new ProjectileStateUpdate(new(121), c.Player.Slot.Value, m.PositionX, m.PositionY,
            8, 0, new(TerraRuntime.Gameplay.Projectiles.VanillaGemStaffProjectile1458.EncodeFeatures(features), 0, 0),
            0, 40, 4, 0);
        Assert.True(shots.TryPrepareVanillaSpawn(in update, null, features, out var preparation));
        Assert.NotNull(preparation);
        Assert.True(preparation.TryCommitUnpublished(out var shot));
        Assert.True(shots.TryMarkCombatTrusted(shot.Handle, c.Player));
        if (noItems)
        {
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new BuffTypeId[] { new(23) })));
            Assert.True(f.Players.TryCaptureProjectileUse(c.Player, out var noItemsOwner));
            Assert.Contains(new BuffTypeId(23), noItemsOwner!.Buffs!);
        }
        var npcs = CaptureNpcs(composition.Npcs);
        var state = new NpcStateUpdate(1, 1, shot.PositionX, shot.PositionY, 0, 0, 0, default,
            NpcSimulationState.Initial with
            {
                Life = 5000,
                LifeMax = 5000,
                LifeRegenCounter = 0,
                Immortal = false,
                MoneyValue = 0,
                ExtraMoneyValue = 0,
                DefenseOverride = 20
            });
        Assert.True(npcs.TrySpawn(0, in state, out var actor));
        var pass = composition.Npcs.ProjectileNpcCombat;
        GemRandom(pass).CopyStateFrom(new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()));
        bool blocked = row.GetProperty("immunity").GetInt32() > 0;
        if (blocked) GemImmunity(pass).MarkHit(shot.Handle, actor.Handle, 0);
        Assert.True(pass.TryProcessGemContacts(in shot, false, f.Tiles, out var result));
        Assert.Equal(blocked ? 0 : 1, result.AcceptedHits);
        Assert.Equal(!blocked, result.SourceMustKill);
        Assert.True(npcs.TryGet(actor.Handle, out var after));
        Assert.Equal(row.GetProperty("afterNpc").GetProperty("life").GetInt32(), after.Simulation.Life);
        if (!blocked)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(row.GetProperty("afterNpc").GetProperty("vx").GetSingle()), BitConverter.SingleToInt32Bits(after.VelocityX));
            Assert.Equal(BitConverter.SingleToInt32Bits(row.GetProperty("afterNpc").GetProperty("vy").GetSingle()), BitConverter.SingleToInt32Bits(after.VelocityY));
        }
        Assert.True(shots.TryGet(result.Current.Handle, out _));
        Assert.True(shots.TryGetLifecycle(result.Current.Handle, out var life));
        Assert.Equal(blocked ? 1 : 0, life.PenetrateOverride);
        var random = row.GetProperty("afterRandom");
        Assert.Equal((uint)random.GetProperty("cursor").GetInt32(), GemCursor(GemRandom(pass)));
        Assert.Equal(random.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray(), GemSeed(GemRandom(pass)));
        Assert.Equal(row.GetProperty("afterNext").GetInt32(), GemRandom(pass).Clone().Next());
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "localNpcImmunity")]
    private static extern ref RuntimeProjectileNpcLocalImmunityRegistry GemImmunity(RuntimeProjectileNpcCombatPass owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] GemSeed(VanillaUnifiedRandom1458 owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint GemCursor(VanillaUnifiedRandom1458 owner);
    public static void Run()
    {
        GemSourceMatrix();
        GemLosSourceMatrix();
        foreach (var mode in new[] { "terminal", "throw", "lethal", "provider-rng", "provider-clear",
            "provider-terrain", "provider-owner", "provider-buff", "overflow", "untrusted" }) GemBoundaryCase(mode);
    }

    internal static void GemSourceMatrix()
    {
        int count = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = Read("GemStaffContact" + platform + "1458");
            foreach (var row in source.RootElement.EnumerateArray()) { GemCase(row); count++; }
            // Same independent clear-source damage/RNG oracle after actual owned packet50
            // installs NoItems23. UpdateBuffs23 has no combat-stat or projectile-damage effect.
            var clear = source.RootElement.EnumerateArray().Single(row =>
                row.GetProperty("mask").GetInt32() == 1 && row.GetProperty("mode").GetInt32() == 0 &&
                row.GetProperty("seed").GetInt32() == 0 && row.GetProperty("immunity").GetInt32() == 0);
            GemCase(clear, noItems: true);
        }
        Console.WriteLine($"GEM_ACTUAL_PVE_SOURCE_PASS cases={count}");
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_commitSink")]
    private static extern ref INpcStateCommitSink? GemNpcSink(RuntimeNpcStore owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "combat")]
    private static extern ref RuntimeNpcNetworkCombatPipeline GemPipeline(RuntimeProjectileNpcCombatPass owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "globalLootWorldSource")]
    private static extern ref Func<RuntimeNpcGlobalLootWorldFacts1458>? GemGlobalSource(RuntimeNpcNetworkCombatPipeline owner);
    private sealed class GemObserver(Action<NpcSnapshot> action) : INpcStateCommitSink
    {
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => action(snapshot);
    }
    internal static void GemBoundaryCase(string mode)
    {
        using var f = new GemContactFixture("empty-selected", runtimeTick: true);
        var c = f.Connections[2];
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c)); f.Tick();
        var m = f.Member(2);
        var composition = Composition(f.Runtime);
        var shots = CaptureProjectiles(composition.Projectiles);
        var features = TerraRuntime.Gameplay.Projectiles.GemStaffFeatures1458.AoeExplosion;
        var update = new ProjectileStateUpdate(new(122), c.Player.Slot.Value, m.PositionX, m.PositionY,
            8, 2, new(TerraRuntime.Gameplay.Projectiles.VanillaGemStaffProjectile1458.EncodeFeatures(features), 0, 0),
            0, 40, 4, 0);
        Assert.True(shots.TryPrepareVanillaSpawn(in update, null, features, out var preparation));
        Assert.NotNull(preparation);
        Assert.True(preparation.TryCommitUnpublished(out var shot));
        if (mode != "untrusted") Assert.True(shots.TryMarkCombatTrusted(shot.Handle, c.Player));
        var npcs = CaptureNpcs(composition.Npcs);
        var state = new NpcStateUpdate(1, 1, shot.PositionX, shot.PositionY, 0, 0, 0, default,
            NpcSimulationState.Initial with
            {
                Life = mode is "lethal" or "provider-rng" or "provider-terrain" or "provider-clear" or "provider-owner" or "provider-buff" ? 1 : 5000,
                LifeMax = 5000,
                LifeRegenCounter = 0,
                Immortal = false,
                MoneyValue = 0,
                ExtraMoneyValue = 0,
                DefenseOverride = 20
            });
        if (mode is "provider-terrain" or "provider-clear") state = state with { PositionX = state.PositionX + 13 };
        Assert.True(npcs.TrySpawn((byte)(mode == "overflow" ? 250 : 0), in state, out var actor));
        var pass = composition.Npcs.ProjectileNpcCombat;
        GemRandom(pass).CopyStateFrom(new VanillaUnifiedRandom1458(0));
        if (mode is "overflow" or "untrusted")
        {
            bool accepted = pass.TryProcessGemContacts(in shot, false, f.Tiles, out var noHit);
            Assert.Equal(mode == "overflow", accepted);
            Assert.Equal(0, noHit.AcceptedHits);
            Assert.True(npcs.TryGet(actor.Handle, out var same));
            Assert.Equal(5000, same.Simulation.Life);
            Assert.Equal(0u, GemCursor(GemRandom(pass)));
            Assert.True(shots.TryGetLifecycle(shot.Handle, out var originalLife));
            Assert.Equal(1, originalLife.PenetrateOverride);
        }
        else if (mode is "provider-terrain" or "provider-clear" or "provider-owner" or "provider-buff")
        {
            int calls = 0;
            var originalWorld = GemGlobalSource(GemPipeline(pass))!;
            var before = GemRandom(pass).Clone();
            GemGlobalSource(GemPipeline(pass)) = () =>
            {
                calls++;
                int x = (int)(shot.PositionX / 16f), y = (int)(shot.PositionY / 16f);
                if (mode == "provider-owner") m.ItemPhase = m.ItemPhase!.Value with { SlowMagicUse = !m.ItemPhase.Value.SlowMagicUse };
                if (mode == "provider-buff") Assert.True(Profiles(f.Players).TrySetBuffTypes(c, new BuffTypeId[] { new(5) }));
                if (mode == "provider-terrain") for (int dx = -3; dx <= 3; dx++) for (int dy = -3; dy <= 3; dy++)
                    f.Tiles.Set(x + dx, y + dy, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                return originalWorld();
            };
            bool accepted = pass.TryProcessGemContacts(in shot, false, f.Tiles, out var resultForProvider);
            Console.WriteLine($"GEM_PROVIDER mode={mode} accepted={accepted} calls={calls}");
            if (mode == "provider-clear") { Assert.True(calls > 0); Assert.Equal(1, resultForProvider.AcceptedHits); Assert.True(accepted); Console.WriteLine("GEM_CONTACT_BOUNDARY_PASS provider-clear"); return; }
            Assert.False(accepted);
            Assert.True(calls > 0);
            Assert.True(npcs.TryGet(actor.Handle, out var after));
            Assert.Equal(1, after.Simulation.Life);
            Assert.True(shots.TryGetLifecycle(shot.Handle, out var afterLife));
            Assert.Equal(1, afterLife.PenetrateOverride);
            Assert.True(GemRandom(pass).HasSameState(before));
        }
        else if (mode == "provider-rng")
        {
            int calls = 0;
            var originalWorld = GemGlobalSource(GemPipeline(pass))!;
            var randomBefore = GemRandom(pass).Clone();
            GemGlobalSource(GemPipeline(pass)) = () =>
            {
                calls++;
                _ = GemRandom(pass).Next();
                return originalWorld();
            };
            Assert.False(pass.TryProcessGemContacts(in shot, false, f.Tiles, out _));
            Assert.True(calls > 0);
            Assert.True(npcs.TryGet(actor.Handle, out var after));
            Assert.Equal(1, after.Simulation.Life);
            Assert.True(shots.TryGetLifecycle(shot.Handle, out var afterLife));
            Assert.Equal(1, afterLife.PenetrateOverride);
            Assert.Equal(0L, afterLife.GemStaffUpdateCount);
            Assert.False(GemRandom(pass).HasSameState(randomBefore));
            var expected = new VanillaUnifiedRandom1458(0);
            for (int i = 0; i < calls; i++) _ = expected.Next();
            Assert.True(GemRandom(pass).HasSameState(expected));
            Assert.False(GemImmunity(pass).IsImmune(shot.Handle, actor.Handle, 0, 60));
        }
        else if (mode == "terminal")
        {
            Assert.False(pass.TryProcessGemContacts(in shot, true, f.Tiles, out _));
            Assert.True(pass.TryProcessGemContacts(in shot, false, f.Tiles, out var initial));
            Assert.Equal(1, initial.AcceptedHits);
            shot = initial.Current;
            Assert.True(shots.TryGetLifecycle(shot.Handle, out var life));
            Assert.True(shots.TryPrepareSimulation(in shot, out var kill));
            Assert.NotNull(kill);
            var killingUpdate = update with
            {
                PositionX = update.PositionX - 75,
                PositionY = update.PositionY - 75,
                Damage = 14,
                KnockBack = 1.4f
            };
            var killingLife = life with { TimeLeft = 0, PenetrateOverride = -1 };
            Assert.True(kill.TryStageGemStaffKill(in killingUpdate, in killingLife));
            Assert.True(kill.TryAdoptUnpublished(out shot, out bool despawn));
            Assert.False(despawn);
            Assert.False(pass.TryProcessGemContacts(in shot, false, f.Tiles, out _));
            var secondState = state with { PositionX = update.PositionX + 60 };
            Assert.True(npcs.TrySpawn(1, in secondState, out var second));
            GemImmunity(pass).ResetGemStaffKillImmunity(shot.Handle, 0);
            Assert.True(pass.TryProcessGemContacts(in shot, true, f.Tiles, out var area));
            Assert.Equal(1, area.AcceptedHits);
            Assert.False(area.SourceMustKill);
            Assert.True(npcs.TryGet(actor.Handle, out var firstAfter));
            Assert.Equal(4966, firstAfter.Simulation.Life);
            Assert.True(npcs.TryGet(second.Handle, out var secondAfter));
            Assert.True(secondAfter.Simulation.Life < 5000);
            Assert.True(shots.TryGet(shot.Handle, out _));
        }
        else
        {
            int observations = 0;
            if (mode == "throw") GemNpcSink(npcs) = new GemObserver(_ =>
            {
                observations++;
                Assert.True(shots.TryGetLifecycle(shot.Handle, out var life));
                Assert.Equal(0, life.PenetrateOverride);
                Assert.True(GemImmunity(pass).IsImmune(shot.Handle, actor.Handle, 0, 60));
                Assert.Equal(4u, GemCursor(GemRandom(pass)));
                throw new InvalidOperationException("configured gem observer");
            });
            if (mode == "throw")
            {
                var error = Assert.Throws<InvalidOperationException>(() => pass.TryProcessGemContacts(in shot, false, f.Tiles, out _));
                Assert.Equal("configured gem observer", error.Message);
                Assert.Equal(1, observations);
                Assert.True(npcs.TryGet(actor.Handle, out var after));
                Assert.Equal(4966, after.Simulation.Life);
            }
            else
            {
                Assert.True(pass.TryProcessGemContacts(in shot, false, f.Tiles, out var result));
                Assert.Equal(1, result.AcceptedHits);
                Assert.True(result.SourceMustKill);
                Assert.Equal(1L, pass.Kills);
                Assert.False(npcs.TryGet(actor.Handle, out _));
                Assert.True(shots.TryGet(result.Current.Handle, out _));
            }
        }
        Console.WriteLine("GEM_CONTACT_BOUNDARY_PASS " + mode);
    }

    internal static void GemLosSourceMatrix()
    {
        int count = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = Read("GemStaffLos" + platform + "1458");
            foreach (var row in source.RootElement.EnumerateArray())
            {
                var query = row.GetProperty("queries")[0];
                using var f = new GemContactFixture("empty-selected", runtimeTick: true);
                var c = f.Connections[2];
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c)); f.Tick();
                foreach (var cell in row.GetProperty("cells").EnumerateArray())
                    f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(),
                        new WorldTile { Type = (ushort)cell.GetProperty("type").GetInt32(), Flags = WorldTileFlags.Active });
                var composition = Composition(f.Runtime);
                var shots = CaptureProjectiles(composition.Projectiles);
                var features = TerraRuntime.Gameplay.Projectiles.GemStaffFeatures1458.AoeExplosion;
                float x = query.GetProperty("fromX").GetSingle(), y = query.GetProperty("fromY").GetSingle();
                var update = new ProjectileStateUpdate(new(122), c.Player.Slot.Value, x - 5, y - 5,
                    8, 2, new(TerraRuntime.Gameplay.Projectiles.VanillaGemStaffProjectile1458.EncodeFeatures(features), 0, 0),
                    0, 14, 1.4f, 0);
                Assert.True(shots.TryPrepareVanillaSpawn(in update, null, features, out var preparation));
                Assert.NotNull(preparation);
                Assert.True(preparation.TryCommitUnpublished(out var shot));
                Assert.True(shots.TryMarkCombatTrusted(shot.Handle, c.Player));
                Assert.True(shots.TryGetLifecycle(shot.Handle, out var life));
                Assert.True(shots.TryPrepareSimulation(in shot, out var kill));
                Assert.NotNull(kill);
                var killingUpdate = update with { PositionX = x - 80, PositionY = y - 80 };
                var killingLife = life with { TimeLeft = 0, PenetrateOverride = -1 };
                Assert.True(kill.TryStageGemStaffKill(in killingUpdate, in killingLife));
                Assert.True(kill.TryAdoptUnpublished(out shot, out bool ended));
                Assert.False(ended);
                var npcs = CaptureNpcs(composition.Npcs);
                var state = new NpcStateUpdate(1, 1, query.GetProperty("toX").GetSingle() - 16,
                    query.GetProperty("toY").GetSingle() - 12, 0, 0, 0, default,
                    NpcSimulationState.Initial with
                    {
                        Life = 5000,
                        LifeMax = 5000,
                        LifeRegenCounter = 0,
                        Immortal = false,
                        MoneyValue = 0,
                        ExtraMoneyValue = 0,
                        DefenseOverride = 20
                    });
                Assert.True(npcs.TrySpawn(0, in state, out var actor));
                var pass = composition.Npcs.ProjectileNpcCombat;
                GemRandom(pass).CopyStateFrom(new VanillaUnifiedRandom1458(0));
                Assert.True(pass.TryProcessGemContacts(in shot, true, f.Tiles, out var result));
                bool expected = query.GetProperty("canHit").GetBoolean();
                Assert.Equal(expected ? 1 : 0, result.AcceptedHits);
                Assert.True(npcs.TryGet(actor.Handle, out var after));
                Assert.Equal(expected, after.Simulation.Life < 5000);
                Assert.Equal(expected ? 4u : 0u, GemCursor(GemRandom(pass)));
                count++;
            }
        }
        Console.WriteLine($"GEM_CONTACT_SOURCE_LOS_PASS cases={count}");
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(GemStaffContact1458Checks).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing original source resource: " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    private sealed class GemContactFixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal ServerRuntimeState Runtime => state!;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly GemContactPlayerEvents Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal GemContactFixture(string mode, bool runtimeTick = false, bool expert = false)
        {
            for (int x = 0; x < 400; x++)
                Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectileReplication = new RuntimeProjectileReplicationRegistry();
            var projectiles = new RuntimeProjectileStore(1000, projectileReplication);
            if (runtimeTick)
            {
                state = new(playerEvents: Events, worldTiles: Tiles, naturalSpawnRandom: new SystemVanillaNpcRandom(new VanillaUnifiedRandom1458(0)), projectiles: projectiles, projectileReplication: projectileReplication, expertMode: expert,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
                    {
                        WorldSurface = 80
                    },
                    playerUpdateRandomSeed: new(0));
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else
            {
                Players = new(Events, Tiles);
                Random = new(0);
            }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            {
                WindowsItemPrefixArithmetic = OperatingSystem.IsWindows()
            });
            Players.SetPlayerUpdateRandom(Random);
            Players.SetRemotePlayerEnvironment(new(expert, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(expert, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }
            var membership = Membership(Players);
            // Slot255 is not a connection lease. A trusted imported source placeholder remains untouched.
            membership.Commit(new RuntimePlayerMember
            {
                Slot = new(255),
                Revision = 1,
                Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))),
                ItemPhase = null,
                PhysicsPhase = null
            });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode is "inactive-first" or "empty-selected")
                    continue;
                var session = sessions[slot];
                session.ObserveWorldRequest();
                session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (mode == "empty-selected" ? (short)0 : (short)28) : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
                if (slot == 2 && mode == "potion-first")
                    id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first")
                {
                    Member(slot).PositionX = 0;
                    Member(slot).PositionY = 0;
                }
                if (slot == 0 && mode == "dead-first")
                    Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first")
                    Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot)
        {
            Assert.True(Players.TryGet(slot, out var member));
            return member;
        }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null)
                Assert.True(Players.TryApply(command));
            else
                state.Apply(command);
        }
        internal void Tick()
        {
            Assert.NotNull(state);
            state.Tick();
        }
        internal bool Step()
        {
            if (state is null)
                return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!);
            sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
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
        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose()
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }
    private sealed class GemContactPlayerEvents : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r)
        {
            Count++;
            Seen.Add(5);
            OnEquipment?.Invoke(c, r);
        }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r)
        {
            Count++;
            Seen.Add(16);
        }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r)
        {
            Count++;
            Seen.Add(42);
        }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r)
        {
            Count++;
            Seen.Add(50);
        }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
