using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ProjectileNpcHitTransaction1458Tests
{
    [Fact]
    public void Explicit_collision_boundary_matches_actual_private_inner_surviving_hits()
    {
        using var rows = Read("ProjectileNpcStatusSelection1458");
        int cases = 0;
        foreach (var row in rows.RootElement.EnumerateArray().Where(row =>
                     row.GetProperty("profile").GetString() == "whole-survive"))
        {
            using var f = new Fixture(row.GetProperty("seed").GetInt32(), row.GetProperty("type").GetInt32());
            f.Pass.Tick();
            Assert.Equal(1, f.Pass.CommittedHits);
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var actor));
            Assert.Equal(row.GetProperty("npcLife").GetInt32(), actor.Simulation.Life);
            AssertStatus(f.Status, actor.Handle, row.GetProperty("buffTypes"), row.GetProperty("buffDurations"));
            // Inner leaves penetration-zero projectiles active; its caller consumes keep=false and kills them.
            Assert.Equal(row.GetProperty("keep").GetBoolean(), f.Shots.TryGet(f.Shot.Handle, out _));
            if (row.GetProperty("keep").GetBoolean())
            {
                Assert.True(f.Shots.TryGetLifecycle(f.Shot.Handle, out var lifecycle));
                Assert.Equal(row.GetProperty("penetration").GetInt32(), lifecycle.PenetrateOverride);
            }
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()),
                f.Drain().Where(frame => frame[2] is 54 or 28).Select(Convert.ToHexString));
            Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Clone().Next());
            cases++;
        }
        // These are actual Damage_PVE_Inner calls, not a claim about the preceding Projectile.Update AI.
        Assert.Equal(12, cases);
    }

    [Fact]
    public void Lava_slime_hit_effect_preserves_actual_private_inner_damage_and_RNG()
    {
        // Independent 1.4.5.8 inner calls: .cache/projectile-npc-lava-hit-proof/facts.json.
        foreach (var sample in new[]
                 {
                     (Type: 2, Seed: 0, Life: 44, Next: 65212616, Frame: "0D001C00010B00000000000200"),
                     (Type: 2, Seed: 1458, Life: 45, Next: 3638822, Frame: "0D001C00010A00000000000200"),
                     (Type: 54, Seed: 0, Life: 44, Next: 65212616, Frame: "0D001C00010B00000000000200"),
                     (Type: 54, Seed: 1458, Life: 45, Next: 3638822, Frame: "0D001C00010A00000000000200")
                 })
        {
            using var f = new Fixture(sample.Seed, sample.Type, 59, 50);
            f.Pass.Tick();
            Assert.Equal(1, f.Pass.CommittedHits);
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var actor));
            Assert.Equal(sample.Life, actor.Simulation.Life);
            Assert.Equal(50, actor.Simulation.LifeMax);
            Assert.True(f.Status.TryCopyWireBuffs(actor.Handle, new TerrariaNpcBuffEntryState[20], out int count));
            Assert.Equal(0, count);
            Assert.Equal(new[] { sample.Frame }, f.Drain().Where(frame => frame[2] is 54 or 28)
                .Select(Convert.ToHexString));
            Assert.Equal(sample.Next, f.Random.Clone().Next());
            Assert.Equal(sample.Type == 54, f.Shots.TryGet(f.Shot.Handle, out _));
        }
    }

    [Fact]
    public void Real_State_tick_admits_source_trusted_fire_hit_with_known_ammo_buffs()
    {
        // Actual contiguous NPC.UpdateNPC then local-owner Projectile.Update, not Main.SwapRandom scheduling.
        using var source = Read("ProjectileNpcStatusCoupled1458");
        var row = source.RootElement.GetProperty("profiles").EnumerateArray().Single(row =>
            row.GetProperty("projectileType").GetInt32() == 2 && row.GetProperty("seed").GetInt32() == 0 &&
            row.GetProperty("life").GetInt32() == 45 && row.GetProperty("value").GetInt32() == 0);
        var random = new VanillaUnifiedRandom1458(0);
        var registry = new RuntimeNpcReplicationRegistry();
        var tiles = new WorldTileStore(new WorldDimensions(100, 80));
        Assert.True(tiles.TryAttachWorldSurface(40));
        for (int x = 0; x < 100; x++) tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        // The real natural-spawn reserve guard excludes its independent pre-NPC phase at capacity eight.
        var npcs = new RuntimeNpcStore(capacity: 8, commitSink: registry);
        var shots = new RuntimeProjectileStore();
        var state = new ServerRuntimeState(npcs: npcs, projectiles: shots, worldTiles: tiles,
            npcReplication: registry, worldClock: new(1000, false, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 40, RockLayer = 35 },
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), playerUpdateRandomSeed: new(0));
        var runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        // The oracle starts at NPC.UpdateNPC/Projectile.Update. This separate empty-selected
        // player phase owns constructor-equivalent crit4 without borrowing the NPC RNG stream.
        runtime.Players.SetRemotePlayerEnvironment(new(false, false), shots);
        runtime.Players.SetNpcHealthWorldFacts(() => new(false, false));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(7254), session.Handle);
        state.Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 400, 400)));
        state.Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 200, 200)));
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 51, 27, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerMovementRuntimeCommand(connection, Movement(session.Slot)));
        state.Apply(new PlayerBuffTypesRuntimeCommand(connection,
            new(session.Slot, new BuffTypeId[] { new(93), new(112) })));
        // State.Tick owns the combined selected phase; the standalone health-only lane
        // does not represent these neutral ammunition buffs.
        Assert.True(npcs.TrySpawn(0, Actor(), out var before));
        Assert.True(shots.TrySpawn(0, Shot(2), out var projectile));
        Assert.True(shots.TryMarkCombatTrusted(projectile.Handle, session.Handle));
        state.Tick();
        Assert.True(runtime.Players.TryGet(connection, out var attacker));
        Assert.Equal(PlayerDerivedCritState1458.SourceBaseline, attacker.ItemPhase!.Value.DerivedCrit);
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(row.GetProperty("hit").GetProperty("life").GetInt32(), after.Simulation.Life);
        AssertStatus(runtime.Npcs.NpcBuffStatus, after.Handle,
            row.GetProperty("hit").GetProperty("buffTypes"), row.GetProperty("hit").GetProperty("buffTimes"));
        Assert.False(shots.TryGet(projectile.Handle, out _));
        Assert.Equal(row.GetProperty("hit").GetProperty("next").GetInt32(), random.Clone().Next());
    }

    [Fact]
    public void Tick_callback_changes_refuse_all_captured_owners_without_rewinding_changed_state()
    {
        foreach (string change in new[] { "inventory", "buffs", "npc", "npcStatus", "projectile", "rng" })
        {
            using var f = new Fixture(0, 54);
            f.BeforeTick = () =>
            {
                switch (change)
                {
                    case "inventory": f.Item(99); break;
                    case "buffs": f.Buffs(93, 112); break;
                    case "npc": f.ChangeNpc(40); break;
                    case "npcStatus": Assert.True(f.Status.TryApply(f.Actor.Handle, new(120), 60)); break;
                    case "projectile": Assert.True(f.Shots.TryDespawn(f.Shot.Handle, out _)); break;
                    default: _ = f.Random.Next(); break;
                }
            };
            var expectedRandom = f.Random.Clone();
            if (change == "rng") _ = expectedRandom.Next();
            f.Pass.Tick();
            Assert.Equal(0, f.Pass.CommittedHits);
            Assert.True(expectedRandom.HasSameState(f.Random));
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var after));
            Assert.Equal(change == "npc" ? 40 : 45, after.Simulation.Life);
            Assert.True(f.Status.TryCopyWireBuffs(after.Handle, new TerrariaNpcBuffEntryState[20], out int count));
            Assert.Equal(change == "npcStatus" ? 1 : 0, count);
            Assert.Equal(change != "projectile", f.Shots.TryGet(f.Shot.Handle, out _));
            if (change == "inventory") Assert.Equal(99, f.Inventory.Stack);
        }
    }

    [Fact]
    public void Known_conservation_buffs_do_not_change_hit_RNG_and_shared_immunity_suppresses_second_hit()
    {
        foreach (int type in new[] { 34, 54 })
        {
            using var baseline = new Fixture(0, type);
            using var buffed = new Fixture(0, type);
            buffed.Buffs(93, 112, 93, 112);
            baseline.Pass.Tick();
            buffed.Pass.Tick();
            Assert.Equal(1, buffed.Pass.CommittedHits);
            Assert.True(baseline.Random.HasSameState(buffed.Random));
            var cursor = buffed.Random.Clone();
            buffed.Pass.Tick();
            Assert.Equal(1, buffed.Pass.CommittedHits);
            Assert.True(cursor.HasSameState(buffed.Random));
            Assert.True(buffed.Shots.TryGetLifecycle(buffed.Shot.Handle, out var lifecycle));
            Assert.Equal(1, lifecycle.PenetrateOverride);
        }
        using var shared = new Fixture(0, 54);
        shared.Pass.Tick();
        Assert.Equal(1, shared.Pass.CommittedHits);
        var sharedCursor = shared.Random.Clone();
        Assert.True(shared.Shots.TrySpawn(1, Shot(34), out var distinctShot));
        Assert.True(shared.Shots.TryMarkCombatTrusted(distinctShot.Handle, shared.Connection.Player));
        Assert.True(shared.Shots.TryGetLifecycle(distinctShot.Handle, out var initialLifecycle));
        shared.Pass.Tick();
        Assert.Equal(1, shared.Pass.CommittedHits);
        Assert.True(sharedCursor.HasSameState(shared.Random));
        Assert.True(shared.Shots.TryGetLifecycle(distinctShot.Handle, out var distinctLifecycle));
        Assert.Equal(initialLifecycle, distinctLifecycle);
    }

    [Fact]
    public void Two_independent_fire_arrows_refresh_accepted_status_without_shared_owner_immunity()
    {
        using var f = new Fixture(0, 2);
        Assert.True(f.Shots.TrySpawn(1, Shot(2), out var second));
        Assert.True(f.Shots.TryMarkCombatTrusted(second.Handle, f.Connection.Player));
        f.Pass.Tick();
        Assert.Equal(2, f.Pass.CommittedHits);
        Assert.Equal(2, f.Pass.ConsumedProjectiles);
        Assert.False(f.Shots.TryGet(f.Shot.Handle, out _));
        Assert.False(f.Shots.TryGet(second.Handle, out _));
        Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var actor));
        Assert.True(actor.Simulation.Life < 37);
        Assert.Equal(2, f.Drain().Count(frame => frame[2] == 28));
    }

    [Fact]
    public void Fire_arrow_status_precedes_strike_then_terminal_projectile_packet()
    {
        using var f = new Fixture(0, 2);
        f.Pass.Tick();
        Assert.Equal(1, f.Pass.CommittedHits);
        Assert.Equal(new byte[] { 54, 28, 29 }, f.Drain()
            .Where(frame => frame[2] is 54 or 28 or 29).Select(frame => frame[2]).ToArray());
        Assert.False(f.Shots.TryGet(f.Shot.Handle, out _));
        Assert.Equal(1, f.Pass.ConsumedProjectiles);
        Assert.Equal(1200195957, f.Random.Clone().Next());
    }

    [Fact]
    public void Unknown_target_status_dependencies_refuse_before_status_and_RNG()
    {
        for (int context = 0; context < 4; context++)
        {
            using var f = new Fixture(0, 2);
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var before));
            var simulation = before.Simulation with
            {
                LifeRegenCounter = context == 0 ? null : before.Simulation.LifeRegenCounter,
                Immortal = context == 1,
                ShimmerTransparency = context == 2 ? null : before.Simulation.ShimmerTransparency,
                LiquidContact = context == 3 ? NpcLiquidContactKind.Shimmer : before.Simulation.LiquidContact
            };
            var update = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
                before.VelocityX, before.VelocityY, before.Target, before.Ai, simulation);
            Assert.True(f.Npcs.TryUpdate(before.Handle, in update, out var imported));
            var random = f.Random.Clone();
            f.Pass.Tick();
            Assert.Equal(0, f.Pass.CommittedHits);
            Assert.True(f.Npcs.TryGet(imported.Handle, out var after));
            Assert.Equal(imported, after);
            Assert.True(random.HasSameState(f.Random));
            Assert.True(f.Shots.TryGet(f.Shot.Handle, out var projectile));
            Assert.Equal(f.Shot, projectile);
            Assert.DoesNotContain(f.Drain(), frame => frame[2] is 54 or 28 or 29);
        }
    }

    [Fact]
    public void Imported_unknown_player_buffs_do_not_gain_source_hit_ownership()
    {
        using var f = new Fixture(0, 54);
        f.ImportUnknownBuffs();
        var cursor = f.Random.Clone();
        f.Pass.Tick();
        Assert.Equal(0, f.Pass.CommittedHits);
        Assert.True(cursor.HasSameState(f.Random));
        Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var actor));
        Assert.Equal(f.Actor, actor);
        Assert.True(f.Shots.TryGet(f.Shot.Handle, out var shot));
        Assert.Equal(f.Shot, shot);
        Assert.DoesNotContain(f.Drain(), frame => frame[2] is 54 or 28 or 29);
    }

    [Fact]
    public void Poison_immune_blue_slime_suppresses_only_status_and_preserves_source_offer_draw()
    {
        using var f = new Fixture(0, 54, npcType: 1);
        f.Pass.Tick();
        Assert.Equal(1, f.Pass.CommittedHits);
        Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var actor));
        Assert.True(actor.Simulation.Life < 45);
        Assert.True(f.Status.TryCopyWireBuffs(actor.Handle, new TerrariaNpcBuffEntryState[20], out int count));
        Assert.Equal(0, count);
        Assert.DoesNotContain(f.Drain(), frame => frame[2] == 54);
        // The source immune fixture still executes crit/variation/hit-point/chance offers.
        Assert.Equal(1200195957, f.Random.Clone().Next());
    }

    [Fact]
    public void Lethal_hit_with_unknown_item_reservation_refuses_every_owner_before_status_publication()
    {
        using var f = new Fixture(0, 2, npcType: 1);
        f.ChangeNpc(1);
        Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var before));
        Assert.True(f.Items.TryReserveDropSlot(out var held));
        Assert.False(f.Combat.Interactions.HasAnyInteraction(f.Actor.Handle));
        var cursor = f.Random.Clone();
        f.Pass.Tick();
        Assert.Equal(0, f.Pass.CommittedHits);
        Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var after));
        Assert.Equal(before, after);
        Assert.True(f.Shots.TryGet(f.Shot.Handle, out var shot));
        Assert.Equal(f.Shot, shot);
        Assert.True(cursor.HasSameState(f.Random));
        Assert.True(f.Status.TryCopyWireBuffs(after.Handle, new TerrariaNpcBuffEntryState[20], out int count));
        Assert.Equal(0, count);
        Assert.DoesNotContain(f.Drain(), frame => frame[2] is 54 or 28);
        Assert.False(f.Combat.Interactions.HasAnyInteraction(f.Actor.Handle));
        Assert.True(f.Items.TryReleaseDropReservation(in held));
    }

    [Fact]
    public void Accepted_lethal_preview_carries_own_interaction_before_status_publication()
    {
        using var f = new Fixture(0, 2, 1);
        f.ChangeNpc(1);
        bool publishedCredit = false;
        f.OnStatus = () => publishedCredit = f.Combat.Interactions.HasAnyInteraction(f.Actor.Handle);
        f.Pass.Tick();
        Assert.Equal(1, f.Pass.Kills);
        Assert.True(publishedCredit);
        Assert.False(f.Npcs.TryGet(f.Actor.Handle, out _));
        Assert.True(f.Items.ActiveCount > 0);
    }

    [Fact]
    public void Status_publication_observes_adopted_penetration_and_preserves_reentrant_changes()
    {
        using var f = new Fixture(0, 54);
        f.OnStatus = () =>
        {
            Assert.True(f.Shots.TryGetLifecycle(f.Shot.Handle, out var lifecycle));
            Assert.Equal(1, lifecycle.PenetrateOverride);
            Assert.Equal(1200195957, f.Random.Clone().Next());
            f.Item(99);
            f.Buffs(93, 112);
            _ = f.Random.Next();
        };
        f.Pass.Tick();
        Assert.Equal(1, f.Pass.CommittedHits);
        Assert.Equal(99, f.Inventory.Stack);
        var sourceAfter = new VanillaUnifiedRandom1458(0);
        for (int index = 0; index < 6; index++) _ = sourceAfter.Next();
        Assert.True(sourceAfter.HasSameState(f.Random));
    }

    [Fact]
    public void Lethal_status_callback_changes_preserve_accepted_items_and_replacement_actors()
    {
        using var baseline = new Fixture(0, 2, 1);
        baseline.ChangeNpc(1);
        baseline.Pass.Tick();
        var expectedItems = new WorldItemSnapshot[baseline.Items.Capacity];
        int expectedCount = baseline.Items.CopyActive(expectedItems);
        Assert.True(expectedCount > 0);
        foreach (string change in new[] { "retire", "revise", "replace" })
        {
            using var f = new Fixture(0, 2, 1);
            f.ChangeNpc(1);
            NpcSnapshot? retained = null;
            VanillaUnifiedRandom1458? publishedRandom = null;
            f.OnStatus = () =>
            {
                publishedRandom = f.Random.Clone();
                if (change == "revise")
                {
                    f.ChangeNpc(30);
                    Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var revised));
                    retained = revised;
                }
                else
                {
                    Assert.True(f.Npcs.TryDespawn(f.Actor.Handle));
                    if (change == "replace")
                    {
                        Assert.True(f.Npcs.TrySpawn(0, Actor(1), out var replacement));
                        retained = replacement;
                    }
                }
            };
            Assert.Null(Record.Exception(f.Pass.Tick));
            Assert.Equal(1, f.Pass.Kills);
            Assert.NotNull(publishedRandom);
            Assert.True(publishedRandom.HasSameState(f.Random));
            var items = new WorldItemSnapshot[f.Items.Capacity];
            Assert.Equal(expectedCount, f.Items.CopyActive(items));
            Assert.Equal(expectedItems.Take(expectedCount), items.Take(expectedCount));
            if (retained is { } actor)
            {
                Assert.True(f.Npcs.TryGet(actor.Handle, out var current));
                Assert.Equal(actor, current);
                if (change == "revise") Assert.True(f.Combat.Interactions.HasAnyInteraction(actor.Handle));
            }
            else Assert.False(f.Npcs.TryGet(f.Actor.Handle, out _));
        }
    }

    [Fact]
    public void Status_callback_retired_or_revised_generations_skip_stale_continuation_without_exception()
    {
        foreach (string change in new[] { "npc-retired", "npc-revised", "shot-retired", "shot-reused" })
        {
            using var f = new Fixture(0, 54);
            ProjectileSnapshot replacement = default;
            f.OnStatus = () =>
            {
                switch (change)
                {
                    case "npc-retired": Assert.True(f.Npcs.TryDespawn(f.Actor.Handle)); break;
                    case "npc-revised": f.ChangeNpc(30); break;
                    case "shot-retired": Assert.True(f.Shots.TryDespawn(f.Shot.Handle, out _)); break;
                    default:
                        Assert.True(f.Shots.TryDespawn(f.Shot.Handle, out _));
                        Assert.True(f.Shots.TrySpawn(0, Shot(54), out replacement));
                        break;
                }
            };
            Assert.Null(Record.Exception(f.Pass.Tick));
            Assert.Equal(1, f.Pass.CommittedHits);
            Assert.Equal(1200195957, f.Random.Clone().Next());
            if (change == "npc-retired") Assert.False(f.Npcs.TryGet(f.Actor.Handle, out _));
            if (change == "npc-revised")
            {
                Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var retained));
                Assert.Equal(30, retained.Simulation.Life);
            }
            if (change == "shot-reused")
            {
                Assert.True(f.Shots.TryGet(replacement.Handle, out var retained));
                Assert.Equal(replacement, retained);
                Assert.True(f.Shots.TryGetLifecycle(replacement.Handle, out var lifecycle));
                Assert.Null(lifecycle.PenetrateOverride);
            }
        }
    }

    private static JsonDocument Read(string name)
    {
        using var resource = typeof(ProjectileNpcHitTransaction1458Tests).Assembly.GetManifestResourceStream(name);
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void AssertStatus(RuntimeNpcBuffStatus1458 status, NpcHandle handle, JsonElement types, JsonElement times)
    {
        var slots = new TerrariaNpcBuffEntryState[20];
        Assert.True(status.TryCopyWireBuffs(handle, slots, out int count));
        Assert.Equal(types.EnumerateArray().Count(type => type.GetInt32() != 0), count);
        for (int index = 0; index < count; index++)
            Assert.Equal(new TerrariaNpcBuffEntryState((ushort)types[index].GetInt32(),
                checked((ushort)times[index].GetInt32())), slots[index]);
    }

    private static NpcStateUpdate Actor(int type = 3, int life = 45) => new(type, (short)type, 800, 440, 0, 0, 0, default,
        NpcSimulationState.Initial with { Life = life, LifeMax = life, Immortal = false, MoneyValue = 0,
            ExtraMoneyValue = 0, ShimmerTransparency = 0 });
    private static ProjectileStateUpdate Shot(int type) => new(new(type), 0, 800, 440, 3, 0, default, 0, 10, 0, 10);
    private static PlayerMovementCommitRequest Movement(PlayerSlotId slot) => new(slot, 0, 0, 0, 0, 0,
        820, 438, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        private readonly TerrariaConnectionOutboundQueue outbound = new(new OutboundQueueOptions(128, 131072, 1024));
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeProjectileStore Shots;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeProjectileNpcCombatPass Pass;
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly NpcSnapshot Actor;
        internal readonly ProjectileSnapshot Shot;
        internal readonly ConnectionHandle Connection;
        internal Action? BeforeTick;
        internal Action? OnStatus;

        internal Fixture(int seed, int projectileType, int npcType = 3, int life = 45)
        {
            Random = new(seed);
            var registry = new RuntimeNpcReplicationRegistry();
            var projectileRegistry = new RuntimeProjectileReplicationRegistry();
            Shots = new(commitSink: projectileRegistry);
            Npcs = new(commitSink: registry);
            var tiles = new WorldTileStore(new WorldDimensions(100, 80));
            Players = new PlayerAuthority(null, tiles);
            var lookup = new RuntimePlayerSnapshotLookup(Players, null);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(7272), session.Handle);
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            Players.TryApply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 200, 200)));
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 51, 27, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerMovementRuntimeCommand(Connection, Movement(session.Slot)));
            Item(5);
            Players.TickHealthContext();
            Assert.True(Npcs.TrySpawn(0, ProjectileNpcHitTransaction1458Tests.Actor(npcType, life), out Actor));
            Status = new(Npcs, handle =>
            {
                registry.PublishNpcBuffs(handle);
                var callback = OnStatus;
                OnStatus = null;
                callback?.Invoke();
            });
            registry.BindNpcBuffStatus(Status);
            Combat = new RuntimeNpcNetworkCombatPipeline(Npcs, Items, lookup, Players,
                () => 100, registry, new(Items), null, new(1000, false, default, 0, 0), new(), false, false,
                lootRandom: Random, seasonalItemContext: () => default);
            Assert.True(Shots.TrySpawn(0, ProjectileNpcHitTransaction1458Tests.Shot(projectileType), out Shot));
            Assert.True(Shots.TryMarkCombatTrusted(Shot.Handle, session.Handle));
            Pass = new(Shots, Npcs, Combat, Players,
                () => { var callback = BeforeTick; BeforeTick = null; callback?.Invoke(); return 100; }, status: Status);
            Assert.True(registry.TryRegister(Connection.Source, outbound));
            Assert.True(projectileRegistry.TryRegister(Connection.Source, outbound));
            registry.PlayerSpawned(Connection, new(session.Slot, 51, 27, 0, 0, 0, 0, 0));
            projectileRegistry.PlayerSpawned(Connection, new(session.Slot, 51, 27, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal RuntimePlayerInventoryItem Inventory
        {
            get
            {
                Assert.True(Players.TryGetInventoryItem(Connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out var item));
                return item;
            }
        }
        internal void Item(short stack) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
            new(session.Slot, VanillaPlayerItemSlotCatalog.AmmoSlotStart, stack, 0, 97, 0)));
        internal void Buffs(params int[] types) => Players.TryApply(new PlayerBuffTypesRuntimeCommand(Connection,
            new(session.Slot, types.Select(type => new BuffTypeId(type)).ToArray())));
        internal void ChangeNpc(int life)
        {
            Assert.True(Npcs.TryGet(Actor.Handle, out var before));
            var update = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
                before.VelocityX, before.VelocityY, before.Target, before.Ai, before.Simulation with { Life = life });
            Assert.True(Npcs.TryUpdate(before.Handle, in update, out _));
        }
        internal void ImportUnknownBuffs()
        {
            var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
            Players.TryApply(new PlayerTransferDetachRuntimeCommand(Connection, detached));
            var transfer = Assert.IsType<RuntimePlayerTransferState>(detached.Task.Result);
            var attached = new TaskCompletionSource<bool>();
            Players.TryApply(new PlayerTransferAttachRuntimeCommand(Connection, transfer with { BuffTypes = null },
                51, 27, false, true, attached));
            Assert.True(attached.Task.Result);
        }
        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }
        public void Dispose() => session.Dispose();
    }
}
