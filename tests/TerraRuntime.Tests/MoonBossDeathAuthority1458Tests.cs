using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class MoonBossDeathAuthority1458Tests
{
    private static readonly int[] BossTypes = [325, 327, 344, 345, 346];

    [Fact]
    public void Dedicated_death_hit_effect_rng_matches_the_independent_original_1458_capture()
        => VerifyMoonHitFixture("MoonBossHit1458", 40);

    private static void VerifyMoonHitFixture(string resourceName, int expectedRows)
    {
        using var resource = typeof(MoonBossDeathAuthority1458Tests).Assembly.GetManifestResourceStream(resourceName)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        var execute = typeof(RuntimeNpcNetworkCombatPipeline).GetMethod("ExecuteNpcDeathHitEffects", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int rows = 0;
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            int type = row.GetProperty("type").GetInt32();
            var f = new Fixture(type, 0, seed: row.GetProperty("seed").GetInt32());
            var target = f.Spawn(type);
            f.Random.CopyStateFrom(new(row.GetProperty("seed").GetInt32()));
            var dead = target with { Simulation = target.Simulation with { Life = 0 } };
            execute.Invoke(f.Pipeline, [dead]);
            Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
            Assert.Empty(f.Drops);
            rows++;
        }
        Assert.Equal(expectedRows, rows);
    }

    [Fact]
    public void Complete_moon_death_loot_money_healing_and_rng_match_original_1458_including_master_value_clear()
        => VerifyCompleteMoonDeathFixture("MoonBossFullDeath1458", 960);

    private static void VerifyCompleteMoonDeathFixture(string resourceName, int expectedRows)
    {
        using var resource = typeof(MoonBossDeathAuthority1458Tests).Assembly.GetManifestResourceStream(resourceName)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int rows = 0;
        bool sawMasterMoneyClear = false, sawMasterMoneyRetained = false;
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            int type = row.GetProperty("type").GetInt32();
            int mode = row.GetProperty("mode").GetInt32();
            bool active = row.GetProperty("active").GetInt32() != 0;
            bool christmas = row.TryGetProperty("christmas", out var seasonal) && seasonal.GetBoolean();
            var f = new Fixture(type, mode, seed: row.GetProperty("seed").GetInt32(), active: active, sourceContext: true,
                christmas: christmas, ownedChristmas: christmas);
            if (active)
                while (f.Clock.MoonEventWaveNumber < 15)
                    Assert.True(f.Clock.TryAdvanceMoonEventDeath(new(type < 338 ? 327 : 345), false, false));
            var target = f.Spawn(type);
            // The original fixture begins at HitEffect, after NewNPC/AI initialization.
            f.Random.CopyStateFrom(new(row.GetProperty("seed").GetInt32()));
            Assert.True(f.Kill(in target, "environment"));
            var expected = row.GetProperty("drops").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, f.Drops.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                var want = expected[index];
                var actual = f.Drops[index];
                Assert.Equal(want.GetProperty("slot").GetInt32(), actual.Handle.Slot);
                Assert.Equal(want.GetProperty("type").GetInt32(), actual.ItemNetId);
                Assert.Equal(want.GetProperty("stack").GetInt32(), actual.Stack);
                Assert.Equal(want.GetProperty("prefix").GetInt32(), actual.Prefix);
                Assert.Equal(want.GetProperty("x").GetSingle(), actual.PositionX);
                Assert.Equal(want.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(want.GetProperty("vx").GetSingle(), actual.VelocityX);
                Assert.Equal(want.GetProperty("vy").GetSingle(), actual.VelocityY);
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Clone().Next());
            var milestones = new[] { VanillaWorldProgressionId.Pumpking, VanillaWorldProgressionId.MourningWood,
                VanillaWorldProgressionId.IceQueen, VanillaWorldProgressionId.SantaNk1, VanillaWorldProgressionId.Everscream };
            Assert.Equal(row.GetProperty("flags").EnumerateArray().Select(flag => flag.GetBoolean()),
                milestones.Select(f.Progression.IsCompleted));
            Assert.True(f.Invasion.TryCapture(out var capture));
            Assert.Equal(row.GetProperty("lantern").GetBoolean(), capture.State.LanternNextNight);
            if (mode == 2)
            {
                bool cleared = row.GetProperty("value").GetSingle() == 0;
                sawMasterMoneyClear |= cleared;
                sawMasterMoneyRetained |= !cleared;
                Assert.Equal(!cleared, f.Drops.Any(drop => drop.ItemNetId is >= 71 and <= 74));
            }
            rows++;
        }
        Assert.Equal(expectedRows, rows);
        Assert.True(sawMasterMoneyClear);
        Assert.True(sawMasterMoneyRetained);
    }

    [Fact]
    public void Every_moon_boss_adopts_flags_credit_items_rng_and_terminal_before_any_observer_in_all_modes()
    {
        foreach (int type in BossTypes)
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (string lane in new[] { "environment", "melee", "projectile" })
        {
            var f = new Fixture(type, mode);
            var target = f.Spawn(type);
            int observations = 0;
            void Observe()
            {
                observations++;
                Assert.True(f.BootstrapAdoptions > 0);
                Assert.False(f.Npcs.TryGet(target.Handle, out _));
                Assert.True(f.Progression.IsCompleted(Milestone(type)));
                Assert.Equal(1UL << (int)Milestone(type), f.Progression.CaptureSnapshot().CompletedMask);
                Assert.Equal(Points(type) * (mode == 2 ? 2.5f : mode == 1 ? 2 : 1), f.Clock.MoonEventTotalInvasionPoints);
                Assert.True(f.Invasion.TryCapture(out var invasion));
                Assert.Equal(type is 327 or 345, invasion.State.LanternNextNight);
                Assert.True(f.Items.ActiveCount > 0);
                Assert.False(f.Random.HasSameState(f.BeforeStrike!));
            }
            f.OnBootstrap = Observe;
            f.OnDrop = Observe;
            f.BeforeStrike = f.Random.Clone();
            Assert.True(f.Kill(in target, lane));
            Assert.True(observations > 0);
            Assert.Single(f.Terminals, handle => handle == target.Handle);
            int count = f.Drops.Count;
            var acceptedRandom = f.Random.Clone();
            Assert.False(f.Kill(in target, lane));
            Assert.Equal(count, f.Drops.Count);
            Assert.True(f.Random.HasSameState(acceptedRandom));
            var frames = Drain(f.First);
            Assert.Equal(28, frames.First()[2]);
            Assert.Equal(23, frames.Last()[2]);
            Assert.Equal(target.Handle.Slot, frames.Last()[3]);
            Assert.DoesNotContain(Drain(f.Join(2)), frame => frame[2] == 23 && frame[3] == target.Handle.Slot);
        }
    }

    [Fact]
    public void Inactive_and_mismatching_events_do_not_grant_boss_milestones_or_lantern_and_unknown_baseline_is_conditional()
    {
        foreach (int type in BossTypes)
        foreach (bool mismatch in new[] { false, true })
        {
            var f = new Fixture(type, 0, active: false, baselineKnown: false);
            if (mismatch) Assert.True(type < 338 ? f.Clock.TryStartSnowMoon() : f.Clock.TryStartPumpkinMoon());
            var target = f.Spawn(type);
            Assert.True(f.Kill(in target, "environment"));
            Assert.False(f.Progression.CaptureSnapshot().HasAny);
            Assert.True(f.Invasion.TryCapture(out var capture));
            Assert.False(capture.State.LanternNextNight);
            Assert.Equal(0f, f.Clock.MoonEventTotalInvasionPoints);
            Assert.DoesNotContain(Drain(f.First), frame => frame[2] is 78 or 82 or 98);
        }
        foreach (int type in BossTypes)
        {
            var unknown = new Fixture(type, 0, baselineKnown: false);
            var target = unknown.Spawn(type);
            var random = unknown.Random.Clone();
            bool accepted = unknown.Kill(in target, "environment");
            Assert.Equal(type is not (327 or 345), accepted);
            if (accepted) Assert.True(unknown.Progression.IsCompleted(Milestone(type)));
            else
            {
                Assert.True(unknown.Npcs.TryGet(target.Handle, out var unchanged));
                Assert.Equal(target, unchanged);
                Assert.True(unknown.Random.HasSameState(random));
                Assert.Empty(unknown.Drops);
                Assert.Equal(0f, unknown.Clock.MoonEventTotalInvasionPoints);
            }
        }
    }

    [Fact]
    public void Loaded_or_live_prior_boss_clear_does_not_schedule_another_lantern_night()
    {
        foreach (int type in new[] { 327, 345 })
        foreach (bool loaded in new[] { false, true })
        {
            var f = new Fixture(type, 0, baselineKnown: loaded, baselineMask: loaded ? 1UL << (int)Milestone(type) : 0);
            if (!loaded) f.Progression.MarkCompleted(Milestone(type));
            f.Progression.MarkCompleted(VanillaWorldProgressionId.Golem);
            var target = f.Spawn(type);
            Assert.True(f.Kill(in target, "projectile"));
            Assert.True(f.Progression.IsCompleted(Milestone(type)));
            Assert.True(f.Progression.IsCompleted(VanillaWorldProgressionId.Golem));
            Assert.True(f.Invasion.TryCapture(out var capture));
            Assert.False(capture.State.LanternNextNight);
        }
    }

    [Fact]
    public void Master_interacting_player_center_requires_known_unchanged_body_profile()
    {
        foreach (int type in BossTypes)
        foreach (int stool in new[] { 4341, 5126 })
        foreach (bool late in new[] { false, true })
        {
            var f = new Fixture(type, 2);
            var target = f.Spawn(type);
            var profiles = (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
                .GetField("transferProfiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Players)!;
            var equipment = new PlayerEquipmentCommitRequest(f.Connection.Player.Slot, 62, 1, 0, (short)stool, 0);
            bool changed = false;
            void ChangeProfile()
            {
                Assert.True(f.Players.TryCapture(f.Connection.Player, out var before));
                Assert.True(profiles.TrySetEquipment(f.Connection, in equipment));
                Assert.True(f.Players.TryCapture(f.Connection.Player, out var after));
                Assert.Equal(before, after); // Public pose revision alone does not own the body profile.
                changed = true;
            }
            if (!late) ChangeProfile();
            var beforeRandom = f.Random.Clone();
            var shot = f.Projectile;
            bool Current()
            {
                if (late && !changed && f.PlayerReadCount >= 600) ChangeProfile();
                return true;
            }
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected,
                f.Pipeline.TryStrikePreparedProjectile(in shot, in target, 0, 100000, 0, false,
                    beforeRandom, beforeRandom.Clone(), Current, _ => Assert.Fail("Unowned Master reward center published.")));
            Assert.True(changed);
            Assert.True(f.Npcs.TryGet(target.Handle, out var unchanged));
            Assert.Equal(target, unchanged);
            Assert.True(f.Random.HasSameState(beforeRandom));
            Assert.Empty(f.Drops);
            Assert.False(f.Progression.CaptureSnapshot().HasAny);
        }
    }

    [Fact]
    public void Nested_different_moon_deaths_use_local_journals_and_do_not_rewind_rng_flags_or_wave_credit()
    {
        foreach (int firstType in new[] { 305, 338, 325, 327, 344, 345, 346 })
        {
            int secondType = firstType < 338 ? 327 : 345;
            var sequential = new Fixture(firstType, 2, seed: 19);
            var first = sequential.Spawn(firstType);
            var second = sequential.Spawn(secondType);
            Assert.True(sequential.Kill(in first, "environment"));
            Assert.True(sequential.Kill(in second, "environment"));

            var nested = new Fixture(firstType, 2, seed: 19);
            var outer = nested.Spawn(firstType);
            var inner = nested.Spawn(secondType);
            bool entered = false;
            VanillaUnifiedRandom1458? afterNested = null;
            nested.OnDrop = () =>
            {
                if (entered) return;
                entered = true;
                Assert.False(nested.Npcs.TryGet(outer.Handle, out _));
                Assert.True(nested.Kill(in inner, "environment"));
                afterNested = nested.Random.Clone();
            };
            Assert.True(nested.Kill(in outer, "environment"));
            Assert.True(entered);
            Assert.True(nested.Random.HasSameState(afterNested!));
            Assert.True(nested.Random.HasSameState(sequential.Random));
            Assert.Equal(sequential.Progression.CaptureSnapshot(), nested.Progression.CaptureSnapshot());
            Assert.Equal(sequential.Clock.MoonEventTotalInvasionPoints, nested.Clock.MoonEventTotalInvasionPoints);
            Assert.Equal(sequential.Clock.MoonEventWaveNumber, nested.Clock.MoonEventWaveNumber);
            Assert.Equal(sequential.Clock.MoonEventWaveKills, nested.Clock.MoonEventWaveKills);
            Assert.Equal(sequential.Drops.Select(DropKey).Order(), nested.Drops.Select(DropKey).Order());
            Assert.Equal(2, nested.Terminals.Count);
            var progress = Drain(nested.First).Where(frame => frame[2] == 78).ToArray();
            Assert.Equal(nested.Clock.MoonEventProgressReportRequired ? 1 : 0, progress.Length);
        }
    }

    [Fact]
    public void Throwing_callbacks_drain_accepted_death_once_and_same_slot_replacement_survives()
    {
        foreach (int type in new[] { 305, 338, 325, 327, 344, 345, 346 })
        foreach (string stage in new[] { "bootstrap", "pendingbirth", "loot" })
        {
            var control = new Fixture(type, 0, seed: 7);
            var controlTarget = control.Spawn(type);
            Assert.True(control.Kill(in controlTarget, "environment"));
            var f = new Fixture(type, 0, seed: 7);
            var target = f.Spawn(type, pending: stage == "pendingbirth");
            bool threw = false;
            void ThrowOnce()
            {
                if (threw) return;
                threw = true;
                throw new InvalidOperationException("retained Moon observer witness");
            }
            if (stage == "bootstrap") f.OnBootstrap = ThrowOnce;
            else if (stage == "pendingbirth") f.OnSpawn = ThrowOnce;
            else f.OnDrop = ThrowOnce;
            var error = Assert.Throws<InvalidOperationException>(() => f.Kill(in target, "environment"));
            Assert.Equal("retained Moon observer witness", error.Message);
            Assert.True(threw);
            Assert.False(f.Npcs.TryGet(target.Handle, out _));
            Assert.Equal(control.Drops.Select(DropKey), f.Drops.Select(DropKey));
            Assert.True(f.Random.HasSameState(control.Random));
            Assert.Single(f.Terminals);
            var frames = Drain(f.First);
            Assert.Equal(23, frames.Last()[2]);
            if (stage == "pendingbirth") Assert.Equal(new byte[] { 23, 28 }, frames.Take(2).Select(frame => frame[2]).ToArray());
            int drops = f.Drops.Count;
            Assert.False(f.Kill(in target, "environment"));
            Assert.Equal(drops, f.Drops.Count);

            var replaced = new Fixture(type, 0);
            var old = replaced.Spawn(type);
            NpcSnapshot replacement = default;
            bool changed = false;
            replaced.OnDrop = () =>
            {
                if (changed) return;
                changed = true;
                var update = new NpcStateUpdate(old.Type, old.NetId, old.PositionX, old.PositionY,
                    old.VelocityX, old.VelocityY, old.Target, old.Ai, old.Simulation);
                Assert.True(replaced.Npcs.TrySpawn(old.Handle.Slot, in update, out replacement));
            };
            Assert.True(replaced.Kill(in old, "environment"));
            Assert.True(changed);
            Assert.Equal(old.Handle.Slot, replacement.Handle.Slot);
            Assert.NotEqual(old.Handle.Generation, replacement.Handle.Generation);
            Assert.True(replaced.Npcs.TryGet(replacement.Handle, out var live));
            Assert.Equal(replacement, live);
            Assert.DoesNotContain(replaced.Terminals, handle => handle == old.Handle);
            var replacementFrames = Drain(replaced.First).Where(frame => frame[2] == 23 && frame[3] == old.Handle.Slot).ToArray();
            Assert.DoesNotContain(replacementFrames, frame => frame[4] == RuntimeNpcPacketProjection.ToProtocolGeneration(old.Handle.Generation));
            var replacementBirth = Assert.Single(replacementFrames);
            Assert.Equal(RuntimeNpcPacketProjection.ToProtocolGeneration(replacement.Handle.Generation), replacementBirth[4]);
            Assert.NotEqual(0, replacementBirth[24] & 8);
        }
    }

    [Fact]
    public void Stale_external_dependencies_refuse_before_hp_reward_or_progress_and_possible_dungeon_spirit_is_fenced()
    {
        foreach (int type in BossTypes)
        foreach (string dependency in new[] { "clock", "progression", "player", "rng", "lantern" })
        {
            if (dependency == "lantern" && type is not (327 or 345)) continue;
            var f = new Fixture(type, 0);
            var target = f.Spawn(type);
            var shot = f.Projectile;
            var before = f.Random.Clone();
            var after = before.Clone();
            after.Next();
            bool mutated = false;
            bool Current()
            {
                // Initial ingress must remain valid; mutate while a retained death is
                // being planned, after its detached world/player/progression capture.
                if (f.PlayerReadCount < 600 || mutated) return true;
                mutated = true;
                if (dependency == "clock") f.Clock.TryAdvanceMoonEventDeath(new(type), false, false);
                else if (dependency == "progression") f.Progression.MarkCompleted(VanillaWorldProgressionId.Golem);
                else if (dependency == "player") f.PlayerPresent = false;
                else if (dependency == "rng") f.Random.Next();
                else
                {
                    Assert.True(f.Invasion.TryCapture(out var capture));
                    var transition = new InvasionTransition1458(capture.State with { LanternNextNight = true }, default);
                    Assert.True(f.Invasion.TryAdopt(in capture, in transition, out _));
                }
                return true;
            }
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected,
                f.Pipeline.TryStrikePreparedProjectile(in shot, in target, 0, 100000, 0, false,
                    before, after, Current, _ => Assert.Fail("Stale death published.")));
            Assert.True(mutated);
            Assert.True(f.Npcs.TryGet(target.Handle, out var unchanged));
            Assert.Equal(target, unchanged);
            Assert.Empty(f.Drops);
            Assert.False(f.Progression.IsCompleted(Milestone(type)));
        }
        foreach (int type in BossTypes)
        foreach (bool? dungeon in new bool?[] { true, null })
        {
            // A real joined human has source-constructor known-false zones even before
            // packet36. The null case deliberately uses the partial lookup's unknown
            // projection rather than relabeling that known constructor state as unknown.
            var f = new Fixture(type, 0, hardMode: true, planteraDowned: true, dungeon: dungeon, ownPlayer: dungeon.HasValue);
            var target = f.Spawn(type);
            Assert.True(f.TryGetPlayer(new(0), out var player));
            Assert.Equal(dungeon, player.Zones?.Dungeon);
            Assert.True(target.Target < 255 && target.Simulation.LifeMax > 100 && target.Simulation.MoneyValue > 0);
            var random = f.Random.Clone();
            Assert.False(f.Kill(in target, "environment"), $"NPC{type}, Dungeon={dungeon?.ToString() ?? "unknown"} must fence the unowned spirit producer.");
            Assert.True(f.Npcs.TryGet(target.Handle, out var unchanged));
            Assert.Equal(target, unchanged);
            Assert.Empty(f.Drops);
            Assert.True(f.Random.HasSameState(random));
        }
    }

    private static float Points(int type) => type switch { 325 => 75, 327 => 150, 344 => 50, 345 => 150, 346 => 100, _ => 1 };
    private static VanillaWorldProgressionId Milestone(int type) => type switch
    {
        325 => VanillaWorldProgressionId.MourningWood, 327 => VanillaWorldProgressionId.Pumpking,
        344 => VanillaWorldProgressionId.Everscream, 345 => VanillaWorldProgressionId.IceQueen,
        _ => VanillaWorldProgressionId.SantaNk1
    };
    private static string DropKey(WorldItemSnapshot drop) => $"{drop.ItemNetId}:{drop.Stack}:{drop.Prefix}:{drop.PositionX}:{drop.PositionY}:{drop.VelocityX}:{drop.VelocityY}";

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly RuntimeNpcReplicationRegistry Registry = new();
        internal readonly RuntimeWorldItemReplicationRegistry ItemRegistry = new();
        internal readonly RuntimeWorldInvasion1458 Invasion = new(new(0, 0, 0, 0, 50, 0, 0, 0, 0, 0, false, false, false, false));
        internal readonly RuntimeWorldProgressionMutations Progression = new();
        internal readonly RuntimeWorldClock Clock;
        internal readonly RuntimeNpcSpawnCycle1458 SpawnCycle = new();
        internal readonly PlayerAuthority Players;
        internal ConnectionHandle Connection;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly TerrariaConnectionOutboundQueue First;
        internal readonly List<WorldItemSnapshot> Drops = [];
        internal readonly List<NpcHandle> Terminals = [];
        internal Action? OnDrop;
        internal Action? OnSpawn;
        internal Action? OnBootstrap;
        internal int BootstrapAdoptions;
        internal int PlayerReadCount;
        internal bool PlayerPresent = true;
        internal VanillaUnifiedRandom1458? BeforeStrike;
        private readonly PlayerStateSnapshot player;
        private readonly PlayerJoinSession? ownedSession;
        private readonly bool sourceContext;
        internal ProjectileSnapshot Projectile => new(new(0, new ProjectileGeneration(1)), new(1), new(14), 0,
            800, 440, 1, 0, default, 0, 32767, 0, 32767);

        internal Fixture(int type, int mode, int seed = 1458, bool active = true, bool baselineKnown = true,
            ulong baselineMask = 0, bool hardMode = false, bool? planteraDowned = false, bool? dungeon = false,
            bool sourceContext = false, bool ownPlayer = true, bool representGlobalWorld = true,
            bool? christmas = false, bool? ownedChristmas = null)
        {
            this.sourceContext = sourceContext;
            Clock = new(0, false, 0, 0, 0,
                forceHalloweenForToday: ownedChristmas.HasValue ? false : null,
                forceXMasForToday: ownedChristmas,
                forceHalloweenForever: ownedChristmas.HasValue ? false : null,
                forceXMasForever: ownedChristmas.HasValue ? false : null,
                seasonDateSource: () => new(2026, 2, 2));
            if (active) Assert.True(type < 338 ? Clock.TryStartPumpkinMoon() : Clock.TryStartSnowMoon());
            Random = new(seed);
            Npcs = new(20, new RuntimeNpcStateCommitFanout(this, Registry));
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            Items = new(this);
            player = default(PlayerStateSnapshot) with
            {
                Player = new(new(0), new(1)), Revision = new(1), PositionX = 100, PositionY = sourceContext ? 150 : 100,
                Zones = dungeon is { } known ? new((byte)(known ? 1 : 0), 0, 0, 0, 0, 0) : null,
                HasHealth = true, Life = sourceContext ? (short)100 : (short)400,
                MaxLife = sourceContext ? (short)100 : (short)400, DerivedLifeMax = sourceContext ? 100 : 400,
                HasMana = true, Mana = sourceContext ? (short)20 : (short)200,
                MaxMana = sourceContext ? (short)20 : (short)200, NpcLifeCurrent = true
            };
            var playerAuthority = new PlayerAuthority(null, null);
            Players = playerAuthority;
            if (!sourceContext && ownPlayer)
            {
                var pool = new PlayerSlotPool(1);
                Assert.True(pool.TryAcquireConnection(out var lease));
                ownedSession = new(lease!);
                ownedSession.ObserveWorldRequest(); ownedSession.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458), ownedSession.Handle);
                Connection = connection;
                Assert.True(playerAuthority.TryApply(new PlayerHealthRuntimeCommand(connection, new(ownedSession.Slot, 400, 400))));
                Assert.True(playerAuthority.TryApply(new PlayerManaRuntimeCommand(connection, new(ownedSession.Slot, 200, 200))));
                Assert.True(playerAuthority.TryApply(new PlayerSpawnRuntimeCommand(connection, ownedSession, new(ownedSession.Slot, 7, 9, 0, 0, 0, 0, 0))));
                if (dungeon is { } representedDungeon)
                    Assert.True(playerAuthority.TryApply(new PlayerZonesRuntimeCommand(connection, new((byte)(representedDungeon ? 1 : 0), 0, 0, 0, 0, 0))));
                Assert.True(playerAuthority.TryCapture(connection.Player, out player));
            }
            Pipeline = new(Npcs, Items, this, playerAuthority, () => 0,
                Registry, new(Items), ItemRegistry, Clock, Progression, mode > 0, mode == 2,
                lootRandom: Random, lootRemixWorld: false, planteraDownedBaseline: planteraDowned,
                seasonalItemContext: ownedChristmas.HasValue ? () => new(false, Clock.ChristmasActive, false) : null,
                globalLootWorld: representGlobalWorld ? new(100, sourceContext ? 80 : 100, mode + 1, hardMode, false, false, christmas, 35, 40, false, false) : null,
                invasion: Invasion, invasionProgressPublisher: _ => Assert.Fail("Moon death changed ordinary invasion progress."),
                progressionBaseline: baselineKnown ? new VanillaWorldProgressionState(baselineMask) : null,
                worldStateAdopted: () => { BootstrapAdoptions++; OnBootstrap?.Invoke(); },
                npcSpawnCycle: SpawnCycle);
            First = Join(1);
        }

        internal NpcSnapshot Spawn(int type, bool pending = false)
        {
            var update = new NpcStateUpdate(type, (short)type, 800, 440, 0, 0, 0, default, NpcSimulationState.Initial);
            NpcSnapshot result;
            Assert.True(pending ? Npcs.TrySpawnVanillaPending(in update, out result) : Npcs.TrySpawnVanilla(in update, out result));
            if (sourceContext)
            {
                var captured = new NpcStateUpdate(result.Type, result.NetId, 600, 300, 0, 0, 0, result.Ai,
                    result.Simulation with { Life = 100, LifeMax = 100, DamageOverride = 20, BaseDamage = 20,
                        DefenseOverride = 0, BaseDefense = 0, HitboxOverride = new(100, 100), MoneyValue = 12345 });
                Assert.True(Npcs.TryUpdate(result.Handle, in captured, out result));
            }
            if (!pending) Drain(First);
            return result;
        }

        internal bool Kill(in NpcSnapshot target, string lane)
        {
            if (lane == "environment") return Pipeline.TryStrikeEnvironment(target.Handle, 100000) == RuntimeTownNpcMeleeDamageResult1458.Killed;
            if (lane == "melee") return Pipeline.TryStrikeServerPlayerMelee(player.Player, target.Handle, 100000, 0, false, 0, 1) == RuntimeProjectileNpcDamageResult.Killed;
            var before = Random.Clone();
            var after = before.Clone();
            after.Next();
            var shot = Projectile;
            return Pipeline.TryStrikePreparedProjectile(in shot, in target, 0, 100000, 0, false, before, after,
                static () => true, _ => { }) == RuntimeProjectileNpcDamageResult.Killed;
        }

        internal TerrariaConnectionOutboundQueue Join(int id)
        {
            var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1024, 524288, 16384));
            var source = GameCommandSourceId.FromConnection(14580 + id);
            Assert.True(Registry.TryRegister(source, queue));
            Assert.True(ItemRegistry.TryRegister(source, queue));
            var connection = new ConnectionHandle(source, new(new((byte)id), new(1)));
            Registry.PlayerSpawned(connection, new(connection.Player.Slot, 1, 1, 0, 0, 0, 0, 0));
            ItemRegistry.PlayerSpawned(connection, new(connection.Player.Slot, 1, 1, 0, 0, 0, 0, 0));
            return queue;
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        { PlayerReadCount++; snapshot = player; return PlayerPresent && slot.Value == 0; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        {
            if (kind == NpcStateCommitKind.Despawn) Terminals.Add(npc.Handle);
            if (kind == NpcStateCommitKind.Spawn) OnSpawn?.Invoke();
        }
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item)
        {
            ItemRegistry.WorldItemStateCommitted(kind, in item);
            if (kind != WorldItemStateCommitKind.Drop) return;
            Drops.Add(item);
            OnDrop?.Invoke();
        }
    }

    private static byte[][] Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
}
