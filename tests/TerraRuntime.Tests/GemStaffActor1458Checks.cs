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

namespace TerraRuntime.Tests.GemStaffSimulation;

public static class GemStaffActor1458Checks
{
    private static JsonDocument ReadSource(string corpus, string platform, string repeat)
    {
        string family = corpus switch
        {
            "immutable-update-v6" => "Update",
            "immutable-impact-v8" => "Impact",
            "immutable-wind-v10" => "Wind",
            "inactive-owner" => "InactiveOwner",
            _ => throw new InvalidOperationException("Unknown source fixture family.")
        };
        string run = repeat == "first" ? "First" : repeat == "repeat" ? "Repeat" : throw new InvalidOperationException("Unknown source capture.");
        using var stream = typeof(GemStaffActor1458Checks).Assembly.GetManifestResourceStream("GemStaffActor" + family + platform + run + "1458");
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    internal static void ActorBoundaryCases()
    {
        foreach (string mode in new[] { "area", "aged-immunity", "throw", "reentry", "replacement", "terminal-revision", "provider-rng", "provider-owner", "bounds", "untrusted", "disconnect", "owner-reuse" })
        {
            using var f = new GemStaffActorFixture("empty-selected", runtimeTick: true);
            var connection = f.Connections[2];
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            f.Tick();
            var composition = Composition(f.Runtime);
            var authority = composition.Projectiles;
            var shots = RootShots(authority);
            var features = TerraRuntime.Gameplay.Projectiles.GemStaffFeatures1458.AoeExplosion;
            var update = new ProjectileStateUpdate(new(122), connection.Player.Slot.Value,
                mode == "bounds" ? 0f : 1000f, 1000f, 8f, 2f,
                new(TerraRuntime.Gameplay.Projectiles.VanillaGemStaffProjectile1458.EncodeFeatures(features), 1f, 0f),
                0, 40, 4f, 0);
            Assert.True(shots.TryPrepareVanillaSpawn(in update, null, features, out var birth));
            Assert.NotNull(birth);
            Assert.True(birth.TryCommitUnpublished(out var projectile));
            if (mode != "untrusted") Assert.True(shots.TryMarkCombatTrusted(projectile.Handle, connection.Player));
            var combat = composition.Npcs.ProjectileNpcCombat;
            var sourceRandom = GemRandom(combat);
            sourceRandom.CopyStateFrom(new VanillaUnifiedRandom1458(0));
            var randomBefore = sourceRandom.Clone();
            if (mode is "bounds" or "untrusted" or "disconnect" or "owner-reuse")
            {
                if (mode is "disconnect" or "owner-reuse") f.Apply(new PlayerDisconnectRuntimeCommand(connection));
                ConnectionHandle replacementOwner = default;
                if (mode == "owner-reuse") replacementOwner = f.ReconnectActorSlot();
                Assert.True(authority.TryTickState(combat));
                Assert.Equal(mode == "untrusted", shots.TryGet(projectile.Handle, out _));
                if (mode is not ("disconnect" or "owner-reuse")) Assert.True(sourceRandom.HasSameState(randomBefore));
                if (mode == "owner-reuse")
                {
                    Assert.True(f.Players.TryGet(replacementOwner.Player, out var replacementMember));
                    Assert.Equal(200, replacementMember.Mana);
                    Assert.False(f.Players.TryGet(connection.Player, out var staleOwner));
                    Assert.NotEqual(connection.Player, replacementOwner.Player);
                }
                Console.WriteLine("ACTOR BOUNDARY " + mode);
                continue;
            }

            var npcs = RootNpcs(composition.Npcs);
            var targetState = new NpcStateUpdate(1, 1, 1008f, 1002f, 0f, 0f, 0, default,
                NpcSimulationState.Initial with { Life = mode.StartsWith("provider") ? 1 : 5000,
                    LifeMax = 5000, LifeRegenCounter = 0, Immortal = false,
                    MoneyValue = 0, ExtraMoneyValue = 0, DefenseOverride = 20 });
            Assert.True(npcs.TrySpawn(0, in targetState, out var first));
            var secondState = targetState with { PositionX = 1065f, Simulation = targetState.Simulation with { Life = 5000 } };
            bool areaTargets = mode is "area" or "aged-immunity" or "throw" or "reentry" or "terminal-revision";
            NpcSnapshot second = default;
            if (areaTargets) Assert.True(npcs.TrySpawn(1, in secondState, out second));
            if (mode == "aged-immunity")
            {
                GemImmunity(combat).MarkHit(projectile.Handle, first.Handle, 0);
                Assert.True(shots.TryGetLifecycle(projectile.Handle, out var life));
                life = life with { TimeLeft = 1 };
                Assert.True(shots.TryPrepareSimulation(in projectile, out var setup));
                Assert.NotNull(setup);
                Assert.True(setup.TryStageMotion(in update, in life));
                Assert.True(setup.TryAdoptUnpublished(out projectile, out bool expired));
                Assert.False(expired);
            }
            int callbacks = 0, providers = 0;
            ProjectileSnapshot replacement = default;
            if (mode.StartsWith("provider"))
            {
                var original = GemGlobalSource(GemPipeline(combat))!;
                GemGlobalSource(GemPipeline(combat)) = () =>
                {
                    providers++;
                    if (mode == "provider-rng") _ = sourceRandom.Next();
                    else
                    {
                        var member = f.Member(2);
                        member.ItemPhase = member.ItemPhase!.Value with { SlowMagicUse = !member.ItemPhase.Value.SlowMagicUse };
                    }
                    return original();
                };
            }
            else if (mode is "throw" or "reentry" or "replacement" or "terminal-revision")
            {
                GemNpcSink(npcs) = new GemObserver(_ =>
                {
                    callbacks++;
                    Assert.True(shots.TryGetLifecycle(projectile.Handle, out var observedLife));
                    Assert.Equal(1L, observedLife.GemStaffUpdateCount);
                    if (mode == "throw") throw new InvalidOperationException("actor-observer-" + callbacks);
                    if (mode == "reentry") Assert.False(authority.TryTickState(combat));
                    if (mode == "terminal-revision" && observedLife.GemStaffKillInProgress)
                    {
                        Assert.True(shots.TryGet(projectile.Handle, out var terminal));
                        var revised = new ProjectileStateUpdate(terminal.Type, terminal.Spawner, terminal.PositionX + 1f, terminal.PositionY, terminal.VelocityX, terminal.VelocityY, terminal.Ai, terminal.BannerIdToRespondTo, terminal.Damage, terminal.KnockBack, terminal.OriginalDamage);
                        Assert.True(shots.TryUpdate(projectile.Handle, in revised, out var changed));
                        Assert.Equal(projectile.Handle, changed.Handle);
                    }
                    if (mode == "replacement")
                    {
                        Assert.True(shots.TryDespawn(projectile.Handle, out var removedProjectile));
                        Assert.True(shots.TrySpawn(projectile.Handle.Slot, in update, out replacement));
                    }
                });
            }
            if (mode == "throw")
            {
                var error = Assert.Throws<InvalidOperationException>(() => authority.TryTickState(combat));
                Assert.Equal("actor-observer-1", error.Message);
            }
            else Assert.True(authority.TryTickState(combat));
            if (mode.StartsWith("provider"))
            {
                Assert.True(providers > 0);
                Assert.Equal(0L, combat.CommittedHits);
                Assert.True(npcs.TryGet(first.Handle, out var unchanged));
                Assert.Equal(1, unchanged.Simulation.Life);
                Assert.True(shots.TryGet(projectile.Handle, out var alive));
                Assert.True(shots.TryGetLifecycle(projectile.Handle, out var after));
                Assert.Equal(1L, after.GemStaffUpdateCount);
                Assert.Equal(299, after.TimeLeft);
                Assert.Equal(1008f, alive.PositionX);
                var expected = new VanillaUnifiedRandom1458(0);
                for (int i = 0; i < 2 + (mode == "provider-rng" ? providers : 0); i++) expected.Next();
                Assert.True(sourceRandom.HasSameState(expected));
            }
            else if (mode == "replacement")
            {
                Assert.True(shots.TryGet(replacement.Handle, out var retained));
                Assert.Equal(replacement, retained);
                Assert.NotEqual(projectile.Handle.Generation, retained.Handle.Generation);
                Assert.False(shots.TryGet(projectile.Handle, out _));
                Assert.Equal(1, callbacks);
            }
            else
            {
                Assert.Equal(2L, combat.CommittedHits);
                Assert.False(shots.TryGet(projectile.Handle, out _));
                Assert.True(npcs.TryGet(first.Handle, out var afterFirst));
                Assert.True(npcs.TryGet(second.Handle, out var afterSecond));
                Assert.True(afterFirst.Simulation.Life < 5000);
                Assert.True(afterSecond.Simulation.Life < 5000);
                var expected = new VanillaUnifiedRandom1458(0);
                // AI two samples, one direct + one area hit (or two area hits), Kill105.
                for (int i = 0; i < 115; i++) expected.Next();
                Assert.True(sourceRandom.HasSameState(expected));
                if (mode is "throw" or "reentry") Assert.Equal(2, callbacks);
            }
            Console.WriteLine("ACTOR BOUNDARY " + mode);
        }
    }

    internal static void ActorSourceCases()
    {
        int cases = 0;
        foreach(string corpus in new[]{"immutable-update-v6","immutable-impact-v8","immutable-wind-v10","inactive-owner"})
        foreach(string platform in new[]{"Linux","Windows"})
        foreach(string repeat in new[]{"first","repeat"})
        {
            using var document = ReadSource(corpus, platform, repeat);
            foreach(var row in document.RootElement.EnumerateArray())
            {
                using var f = new GemStaffActorFixture("empty-selected", runtimeTick:true, windows:platform=="Windows");
                var connection=f.Connections[2];
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));f.Tick();
                for(int x=60;x<90;x++)for(int y=45;y<90;y++)
                {
                    string terrain=row.GetProperty("terrain").GetString()!;
                    bool active=terrain is "wall" or "expiry-wall"&&x==(corpus=="inactive-owner"?63:64)||terrain is "floor" or "slope" or "expiry-floor"&&y==64;
                    var tile=new WorldTile{Type=1,Flags=active?WorldTileFlags.Active:0,
                        LiquidAmount=(byte)(terrain is "water" or "honey"?255:0),LiquidKind=terrain=="honey"?WorldLiquidKind.Honey:WorldLiquidKind.Water,
                        Shape=(byte)(terrain=="slope"&&active?2:0)};
                    f.Tiles.Set(x,y,in tile);
                }
                var composition=Composition(f.Runtime);var authority=composition.Projectiles;var shots=RootShots(authority);
                if (row.TryGetProperty("windPhysics", out var sourceWind))
                {
                    var worldStepper = Assert.IsType<VanillaProjectileWorldStateStepper>(ActorStepper(authority));
                    worldStepper.SetWindPhysics(sourceWind.GetBoolean(), row.GetProperty("windSpeedCurrent").GetSingle(), row.GetProperty("windPhysicsStrength").GetSingle());
                }
                var initial=row.GetProperty("steps")[0].GetProperty("before");var body=initial.GetProperty("body");
                var features=(TerraRuntime.Gameplay.Projectiles.GemStaffFeatures1458)row.GetProperty("mask").GetInt32();
                var update=new ProjectileStateUpdate(new(row.GetProperty("type").GetInt32()),connection.Player.Slot.Value,
                    body.GetProperty("position").GetProperty("x").GetSingle(),body.GetProperty("position").GetProperty("y").GetSingle(),
                    body.GetProperty("velocity").GetProperty("x").GetSingle(),body.GetProperty("velocity").GetProperty("y").GetSingle(),
                    new(BitConverter.Int32BitsToSingle(body.GetProperty("aiBits")[0].GetInt32()),BitConverter.Int32BitsToSingle(body.GetProperty("aiBits")[1].GetInt32()),BitConverter.Int32BitsToSingle(body.GetProperty("aiBits")[2].GetInt32())),
                    0,(short)body.GetProperty("damage").GetInt32(),body.GetProperty("knockBack").GetSingle(),0);
                Assert.True(shots.TryPrepareVanillaSpawn(in update,null,features,out var birth));Assert.NotNull(birth);
                Assert.True(birth.TryCommitUnpublished(out var actor));Assert.True(shots.TryMarkCombatTrusted(actor.Handle,connection.Player));
                Assert.True(shots.TryGetLifecycle(actor.Handle,out var life));
                life=life with { TimeLeft=body.GetProperty("timeLeft").GetInt32(),PenetrateOverride=body.GetProperty("penetrate").GetInt32(),
                    GemStaff=TerraRuntime.Gameplay.Projectiles.GemStaffRuntimeState1458.Constructor with { Scale=body.GetProperty("scale").GetSingle(),Rotation=body.GetProperty("rotation").GetSingle()},
                    Liquid=new(initial.GetProperty("wet").GetBoolean(),initial.GetProperty("lavaWet").GetBoolean(),initial.GetProperty("honeyWet").GetBoolean(),initial.GetProperty("shimmerWet").GetBoolean())};
                Assert.True(shots.TryPrepareSimulation(in actor,out var setup));Assert.NotNull(setup);
                Assert.True(setup.TryStageMotion(in update,in life));Assert.True(setup.TryAdoptUnpublished(out actor,out bool expired));Assert.False(expired);
                var sourceRandom=GemRandom(composition.Npcs.ProjectileNpcCombat);
                sourceRandom.CopyStateFrom(new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()));
                var publications = new List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)>();
                NpcSnapshot inactiveTarget = default;
                if (corpus == "inactive-owner")
                {
                    if (row.GetProperty("targetMode").GetString() != "none")
                    {
                        var raw = row.GetProperty("targetBefore");
                        var position = raw.GetProperty("position").GetString()!.Split(',').Select(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        var state = new NpcStateUpdate((short)raw.GetProperty("type").GetInt32(), (short)raw.GetProperty("type").GetInt32(), position[0], position[1], 0, 0, 0, default,
                            NpcSimulationState.Initial with { Life = raw.GetProperty("life").GetInt32(), LifeMax = 5000, DontTakeDamage = raw.GetProperty("dontTakeDamage").GetBoolean(), Immortal = false });
                        var npcs = RootNpcs(composition.Npcs);
                        Assert.True(npcs.TrySpawn(0, in state, out inactiveTarget));
                        Assert.True(TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(inactiveTarget.TypeIdentity, inactiveTarget.NetIdentity, out var definition));
                        Assert.True(definition.TryResolveHitbox(inactiveTarget.Simulation, out var size));
                        // Source captures an explicit32x48 target. This supported runtime body
                        // represents its exact center; inactive-owner AI only reads that center.
                        state = state with { PositionX = position[0] + raw.GetProperty("width").GetInt32() / 2f - size.Width / 2f,
                            PositionY = position[1] + raw.GetProperty("height").GetInt32() / 2f - size.Height / 2f };
                        Assert.True(npcs.TryUpdate(inactiveTarget.Handle, in state, out inactiveTarget));
                        if (!raw.GetProperty("active").GetBoolean()) Assert.True(npcs.TryDespawn(inactiveTarget.Handle));
                    }
                    var previousSink = ActorProjectileSink(shots);
                    ActorProjectileSink(shots) = new ActorProjectileObserver(previousSink, (kind, snapshot) => publications.Add((kind, snapshot)));
                    f.Apply(new PlayerDisconnectRuntimeCommand(connection));
                }
                foreach(var step in row.GetProperty("steps").EnumerateArray())
                {
                    try
                    {
                        Assert.True(authority.TryTickState(composition.Npcs.ProjectileNpcCombat));
                        var after=step.GetProperty("after");var expected=after.GetProperty("body");
                        bool active=expected.GetProperty("active").GetBoolean();Assert.Equal(active,shots.TryGet(actor.Handle,out var actual));
                        if(active)
                        {
                            Assert.True(shots.TryGetLifecycle(actor.Handle,out var actualLife));
                            Assert.Equal(after.GetProperty("positionBits")[0].GetInt32(),BitConverter.SingleToInt32Bits(actual.PositionX));
                            Assert.Equal(after.GetProperty("positionBits")[1].GetInt32(),BitConverter.SingleToInt32Bits(actual.PositionY));
                            Assert.Equal(after.GetProperty("velocityBits")[0].GetInt32(),BitConverter.SingleToInt32Bits(actual.VelocityX));
                            Assert.Equal(after.GetProperty("velocityBits")[1].GetInt32(),BitConverter.SingleToInt32Bits(actual.VelocityY));
                            Assert.Equal(expected.GetProperty("timeLeft").GetInt32(),actualLife.TimeLeft);
                            Assert.Equal(expected.GetProperty("penetrate").GetInt32(),actualLife.PenetrateOverride);
                            Assert.Equal(expected.GetProperty("singleBits").GetProperty("scale").GetInt32(),BitConverter.SingleToInt32Bits(actualLife.GemStaff!.Value.Scale));
                            Assert.Equal(expected.GetProperty("singleBits").GetProperty("rotation").GetInt32(),BitConverter.SingleToInt32Bits(actualLife.GemStaff!.Value.Rotation));
                            Assert.Equal(after.GetProperty("wet").GetBoolean(),actualLife.Liquid.Wet);
                            for(int i=0;i<6;i++)
                            {
                                Assert.Equal(after.GetProperty("oldPos")[i].GetProperty("xb").GetInt32(),BitConverter.SingleToInt32Bits(actualLife.GemStaff.Value.Trail[i].X));
                                Assert.Equal(after.GetProperty("oldPos")[i].GetProperty("yb").GetInt32(),BitConverter.SingleToInt32Bits(actualLife.GemStaff.Value.Trail[i].Y));
                            }
                        }
                        if (corpus == "inactive-owner")
                        {
                            Assert.Single(publications);
                            Assert.Equal(ProjectileStateCommitKind.Remove, publications[0].Kind);
                            var removed = publications[0].Snapshot;
                            Assert.Equal(after.GetProperty("positionBits")[0].GetInt32(), BitConverter.SingleToInt32Bits(removed.PositionX));
                            Assert.Equal(after.GetProperty("positionBits")[1].GetInt32(), BitConverter.SingleToInt32Bits(removed.PositionY));
                            Assert.Equal(after.GetProperty("velocityBits")[0].GetInt32(), BitConverter.SingleToInt32Bits(removed.VelocityX));
                            Assert.Equal(after.GetProperty("velocityBits")[1].GetInt32(), BitConverter.SingleToInt32Bits(removed.VelocityY));
                            Assert.Equal(expected.GetProperty("damage").GetInt32(), removed.Damage);
                            Assert.Equal(0L, composition.Npcs.ProjectileNpcCombat.CommittedHits);
                            if (inactiveTarget.IsActive)
                            {
                                var retainedSlots = new TerraRuntime.Core.Npcs.VanillaNpcRetainedSlot[RootNpcs(composition.Npcs).Capacity];
                                RootNpcs(composition.Npcs).CopyRetainedSlots(retainedSlots);
                                Assert.Equal(5000, retainedSlots[inactiveTarget.Handle.Slot].Simulation.Life);
                            }
                        }
                        Assert.Equal(step.GetProperty("afterRandom").GetProperty("cursor").GetUInt32(),GemCursor(sourceRandom));
                        Assert.Equal(step.GetProperty("afterRandom").GetProperty("state").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),GemCells(sourceRandom));
                        Assert.Equal(step.GetProperty("afterNext").GetInt32(),sourceRandom.Clone().Next());
                        cases++;
                    }
                    catch {Console.WriteLine($"ACTORFAIL {corpus}/{platform}/{repeat} mask{(int)features} {row.GetProperty("terrain")} tick{step.GetProperty("tick")}");throw;}
                }
            }
        }
        Assert.Equal(3144, cases);
        Console.WriteLine("ACTOR ORIGINAL UPDATE CASES "+cases);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] GemCells(VanillaUnifiedRandom1458 random);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateWorld")]
    private static extern ref RuntimePlayerUpdateWorld1458? FixtureWorld(PlayerAuthority players);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_commitSink")]
    private static extern ref IProjectileStateCommitSink? ActorProjectileSink(RuntimeProjectileStore owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "stepper")]
    private static extern ref IProjectileStateStepper? ActorStepper(ProjectileAuthority owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "npcs")]
    private static extern ref RuntimeNpcStore RootNpcs(NpcAuthority owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "projectiles")]
    private static extern ref RuntimeProjectileStore RootShots(ProjectileAuthority owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "sourceRandom")]
    private static extern ref VanillaUnifiedRandom1458 GemRandom(RuntimeProjectileNpcCombatPass owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "localNpcImmunity")]
    private static extern ref RuntimeProjectileNpcLocalImmunityRegistry GemImmunity(RuntimeProjectileNpcCombatPass owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] GemSeed(VanillaUnifiedRandom1458 owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint GemCursor(VanillaUnifiedRandom1458 owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_commitSink")]
    private static extern ref INpcStateCommitSink? GemNpcSink(RuntimeNpcStore owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "combat")]
    private static extern ref RuntimeNpcNetworkCombatPipeline GemPipeline(RuntimeProjectileNpcCombatPass owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "globalLootWorldSource")]
    private static extern ref Func<RuntimeNpcGlobalLootWorldFacts1458>? GemGlobalSource(RuntimeNpcNetworkCombatPipeline owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "durations")]
    private static extern ref int[] BuffDurations(PlayerBuffState state);

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

    private sealed class GemObserver(Action<NpcSnapshot> action) : INpcStateCommitSink
    {
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => action(snapshot);
    }

    private sealed class ActorProjectileObserver(IProjectileStateCommitSink? previous, Action<ProjectileStateCommitKind, ProjectileSnapshot> action) : IProjectileStateCommitSink
    {
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            previous?.ProjectileStateCommitted(kind, in snapshot);
            action(kind, snapshot);
        }
    }

    private sealed class GemStaffActorFixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal ServerRuntimeState Runtime => state!;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly GemStaffActorPlayerSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal GemStaffActorFixture(string mode, bool runtimeTick = false, bool expert = false, bool windows = false)
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
            // Explicit source-platform arithmetic fixture before any owned phase; not a public rebind.
            FixtureWorld(Players) = new(400, 300, 80, false, false) { WindowsItemPrefixArithmetic = windows };
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
        internal ConnectionHandle ReconnectActorSlot()
        {
            sessions[2].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!);
            sessions.Add(session);
            Assert.Equal(new PlayerSlotId(2), session.Slot);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99122), session.Handle);
            Connections[2] = connection;
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 400, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 200, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 1, 0, 739, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            return connection;
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

    private sealed class GemStaffActorPlayerSink : IRuntimePlayerEventSink
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
