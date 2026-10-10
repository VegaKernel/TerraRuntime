using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

using System.IO.Compression;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class OrdinaryArrowContinuationSource1458Tests
{
    // Original configured local0/net2 headless Projectile.Update, key generation explicitly1.
    // Types1/2 remain source references. Only4/5 canonical nonlethal Zombie3 are routed here.
    // Original28 observers see pre-Strike HP; runtime observers deliberately see all final adopted owners.
    // Source-only oldPosition/numUpdates/full256 immunity/persistent numHits are not invented runtime fields.
    private static void Check(bool condition, string label) => Assert.True(condition, label);
    private static void RandomEquals(VanillaUnifiedRandom1458 actual, JsonElement expected, string label)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Check((uint)typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!.GetValue(actual)! == expected.GetProperty("cursor").GetUInt32(), label + "/cursor");
        Check(((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!.GetValue(actual)!).SequenceEqual(
            expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32())), label + "/full56");
        Check(actual.Clone().Next() == expected.GetProperty("next").GetInt32(), label + "/next");
    }
    private static ProjectileAuthority Authority(ArrowWholeUpdateSourceSupport1458.Fixture f, VanillaUnifiedRandom1458? random = null) =>
        new(f.Shots, f.Players, f.Npcs, new RuntimePlayerSnapshotLookup(f.Players, null),
            new VanillaProjectileWorldStateStepper(f.Tiles), f.ProjectileRegistry, () => 100,
            worldTiles: f.Tiles, projectileRandom: random ?? f.Random, npcReplication: f.NpcRegistry);
    private static byte[][] DrainQueue(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
    private static byte[] Encode(ProjectileSnapshot snapshot)
    {
        Check(RuntimeProjectilePacketProjection.TryCreateUpdate(snapshot, out var packet), "final/project");
        Check(TerrariaProjectileEncoder.TryEncodeUpdate(packet, out var bytes), "final/encode");
        return bytes;
    }
    private static void Compare(ArrowWholeUpdateSourceSupport1458.Fixture f, JsonElement row, string label)
    {
        Check(f.Shots.TryGet(f.Shot.Handle, out var actual), label + "/active");
        var expected = row.GetProperty("after");
        Check(actual.PositionX == expected.GetProperty("position").GetProperty("X").GetSingle(), label + "/x");
        Check(actual.PositionY == expected.GetProperty("position").GetProperty("Y").GetSingle(), label + "/y");
        Check(actual.VelocityX == expected.GetProperty("velocity").GetProperty("X").GetSingle(), label + "/vx");
        Check(actual.VelocityY == expected.GetProperty("velocity").GetProperty("Y").GetSingle(), label + "/vy");
        Check(actual.Damage == expected.GetProperty("damage").GetInt32(), label + "/damage");
        Check(actual.KnockBack == expected.GetProperty("knockBack").GetSingle(), label + "/knockBack");
        Check(actual.OriginalDamage == expected.GetProperty("originalDamage").GetInt32(), label + "/originalDamage");
        Check(actual.Spawner == expected.GetProperty("owner").GetInt32(), label + "/owner");
        for (int i = 0; i < 3; i++)
        {
            float a = i == 0 ? actual.Ai.Ai0 : i == 1 ? actual.Ai.Ai1 : actual.Ai.Ai2;
            Check(BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(expected.GetProperty("ai")[i].GetSingle()), label + "/ai" + i);
        }
        Check(f.Shots.TryGetLifecycle(actual.Handle, out var lifecycle), label + "/lifecycle");
        Check(lifecycle.TimeLeft == expected.GetProperty("timeLeft").GetInt32(), label + "/time");
        Check(lifecycle.OldVelocityX == expected.GetProperty("oldVelocity").GetProperty("X").GetSingle() &&
            lifecycle.OldVelocityY == expected.GetProperty("oldVelocity").GetProperty("Y").GetSingle(), label + "/oldVelocity");
        for (int i = 0; i < 3; i++)
        {
            float value = i == 0 ? lifecycle.LocalAi.Ai0 : i == 1 ? lifecycle.LocalAi.Ai1 : lifecycle.LocalAi.Ai2;
            Check(BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(expected.GetProperty("localAi")[i].GetSingle()), label + "/localAi" + i);
        }
        Check(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(actual.Type, out var penetration), label + "/penMetadata");
        Check((lifecycle.PenetrateOverride ?? penetration) == expected.GetProperty("penetration").GetInt32(), label + "/pen");
        var buffer = new NpcSnapshot[f.Npcs.Capacity];
        int count = f.Npcs.CopyActive(buffer);
        Check(buffer.Take(count).Select(n => n.Simulation.Life).SequenceEqual(row.GetProperty("targetsAfter").EnumerateArray().Select(t => t.GetProperty("life").GetInt32())), label + "/HP");
        RandomEquals(f.Random, row.GetProperty("afterRandom"), label + "/random");
    }
    private static byte[][] ReplayBaseline(ArrowWholeUpdateSourceSupport1458.Fixture f)
    {
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 131072, 1024));
        var source = GameCommandSourceId.FromConnection(9393);
        Check(f.ProjectileRegistry.TryRegister(source, queue), "baseline/register");
        f.ProjectileRegistry.PlayerSpawned(new ConnectionHandle(source, f.Connection.Player), new(new(0), 51, 27, 0, 0, 0, 0, 0));
        var frames = DrainQueue(queue);
        Check(f.ProjectileRegistry.TryUnregister(source), "baseline/unregister");
        return frames;
    }
    private static JsonDocument Load(string key)
    {
        using var stream = typeof(OrdinaryArrowContinuationSource1458Tests).Assembly.GetManifestResourceStream("OrdinaryArrowContinuation1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        return JsonDocument.Parse(document.RootElement.GetProperty(key).GetRawText());
    }

    [Fact]
    public void Actual_authority_route_preserves_source_temporal_hits_wire_and_final_join_baseline()
    {
        using var source = Load("rows");
        int rows = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            int type = row.GetProperty("shotType").GetInt32();
            if (type is not (4 or 5)) continue;
            string label = $"{type}/{row.GetProperty("geometry").GetString()}/{row.GetProperty("seed").GetInt32()}";
            using var f = new ArrowWholeUpdateSourceSupport1458.Fixture(row.GetProperty("seed").GetInt32(), type, row);
            var authority = Authority(f);
            int observers = 0;
            var join = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 131072, 1024));
            byte[][] firstJoin = [];
            f.OnNpcCommit = () =>
            {
                observers++;
                Compare(f, row, label + "/observerFinal");
                var ledger = (RuntimeNpcPlayerInteractionLedger)typeof(RuntimeNpcNetworkCombatPipeline)
                    .GetField("interactions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Combat)!;
                foreach (var target in row.GetProperty("targetsAfter").EnumerateArray().Where(target => target.GetProperty("life").GetInt32() < 1000))
                {
                    Check(f.Npcs.TryGetActive((byte)target.GetProperty("slot").GetInt32(), out var accepted), label + "/observerTarget");
                    Check(ledger.HasInteraction(accepted.Handle, f.Connection.Player.Slot), label + "/observerAllCredits");
                }
                if (observers == 1)
                {
                    Check(f.ProjectileRegistry.TryRegister(GameCommandSourceId.FromConnection(8282), join), label + "/joinRegister");
                    f.ProjectileRegistry.PlayerSpawned(new ConnectionHandle(GameCommandSourceId.FromConnection(8282), f.Connection.Player), new(new(0), 51, 27, 0, 0, 0, 0, 0));
                    firstJoin = DrainQueue(join);
                    Check(f.Shots.TryGet(f.Shot.Handle, out var final), label + "/observerShot");
                    Check(firstJoin.Length == 1 && firstJoin[0].SequenceEqual(Encode(final)), label + "/firstJoinFinalBaseline");
                }
            };
            Check(authority.TryTickState(f.Pass), label + "/realTick");
            Compare(f, row, label);
            Check(observers == row.GetProperty("publicationObservations").EnumerateArray().Count(o => Convert.FromHexString(o.GetProperty("hex").GetString()!)[2] == 28), label + "/observers");
            var expectedFrames = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
            var frames = f.Drain().Where(x => x[2] is 27 or 28).ToArray();
            Check(frames.Length == expectedFrames.Length, label + "/framesCount");
            for (int i = 0; i < frames.Length; i++) Check(frames[i].SequenceEqual(expectedFrames[i]), label + "/literal" + i);
            _ = DrainQueue(join);
            f.ProjectileRegistry.PlayerSpawned(new ConnectionHandle(GameCommandSourceId.FromConnection(8282), f.Connection.Player), new(new(0), 51, 27, 0, 0, 0, 0, 0));
            var replay = DrainQueue(join);
            Check(replay.Length == 1 && replay[0].SequenceEqual(firstJoin[0]), label + "/historicalDidNotReplaceFinalBaseline");
            f.OnNpcCommit = null;
            var checkpoint = f.Random.Clone();
            f.Pass.Tick();
            Check(f.Random.HasSameState(checkpoint) && f.Drain().Length == 0, label + "/outerPassNoDuplicates");
            Compare(f, row, label + "/afterOuterPass");
            rows++;
        }
        Check(rows == 18, "source18");

        int noContactRows = 0;
        using (var corpus = Load("noContact"))
        {
            foreach (var row in corpus.RootElement.EnumerateArray())
            {
                using var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, row.GetProperty("shotType").GetInt32(), row);
                Check(Authority(f).TryTickState(f.Pass), "noContact/actualTick");
                Compare(f, row, "noContact");
                Check(f.Drain().Length == 0, "noContact/sourceNoFrames");
                var checkpoint = f.Random.Clone();
                f.Pass.Tick();
                Check(f.Random.HasSameState(checkpoint) && f.Drain().Length == 0, "noContact/noOuterFallback");
                noContactRows++;
            }
        }
        int sharedRows = 0;
        using (var corpus = Load("sharedImmunity"))
        {
            foreach (var row in corpus.RootElement.EnumerateArray())
            {
                var first = row.GetProperty("firstUpdate");
                using var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, row.GetProperty("shotType").GetInt32(), first);
                var authority = Authority(f);
                Check(authority.TryTickState(f.Pass), "shared/firstActualTick");
                Compare(f, first, "shared/first");
                Check(f.Shots.TryDespawn(f.Shot.Handle, out _), "shared/configuredReset");
                var initial = f.Shot;
                var state = new ProjectileStateUpdate(initial.Type, initial.Spawner, initial.PositionX, initial.PositionY,
                    initial.VelocityX, initial.VelocityY, initial.Ai, initial.BannerIdToRespondTo, initial.Damage, initial.KnockBack, initial.OriginalDamage);
                Check(f.Shots.TrySpawn(0, state, out var fresh) && f.Shots.TryMarkCombatTrusted(fresh.Handle, f.Connection.Player), "shared/configuredSecond");
                _ = f.Drain(); // Component setup births/removal are not claimed as original between-actor publications.
                Check(authority.TryTickState(f.Pass), "shared/secondActualTick");
                Check(f.Npcs.TryGet(f.Actor.Handle, out var target) && target.Simulation.Life == row.GetProperty("targetsAfter")[0].GetProperty("life").GetInt32(), "shared/HP");
                RandomEquals(f.Random, row.GetProperty("afterRandom"), "shared/full56");
                Check(f.Drain().Length == 0, "shared/sourceNoFrames");
                var checkpoint = f.Random.Clone();
                f.Pass.Tick();
                Check(f.Random.HasSameState(checkpoint) && f.Drain().Length == 0, "shared/noOuterFallback");
                sharedRows++;
            }
        }

        Check(noContactRows == 2 && sharedRows == 2, "bounded additional original references");
    }

    [Fact]
    public void Initial_terminal_fallback_and_prepared_currentness_remain_bounded_under_reentry()
    {
        using var source = Load("rows");
        var guardRow = source.RootElement.EnumerateArray().Single(r => r.GetProperty("shotType").GetInt32() == 5 && r.GetProperty("geometry").GetString() == "two-overlap" && r.GetProperty("seed").GetInt32() == 0);
        NpcStateUpdate NpcUpdate(NpcSnapshot n, float offset = 0) => new(n.TypeIdentity.Value, (short)n.NetIdentity.Value,
            n.PositionX + offset, n.PositionY, n.VelocityX, n.VelocityY, n.Target, n.Ai, n.Simulation);
        ProjectileStateUpdate ShotUpdate(ProjectileSnapshot p, float offset = 0) => new(p.Type, p.Spawner,
            p.PositionX + offset, p.PositionY, p.VelocityX, p.VelocityY, p.Ai, p.BannerIdToRespondTo, p.Damage, p.KnockBack, p.OriginalDamage);
        foreach (string mutation in new[] { "npcABA", "shotABA", "rng", "terrain", "player", "status", "ledger" })
        {
            using var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, guardRow);
            Check(f.Pass.TryPrepareOrdinaryArrowContinuation(f.Shot, new VanillaProjectileWorldStateStepper(f.Tiles), out var plan, out _) && plan is not null, mutation + "/prepare");
            using (plan!)
            {
                if (mutation == "npcABA")
                {
                    Check(f.Npcs.TryUpdate(f.Actor.Handle, NpcUpdate(f.Actor, 1), out _), mutation + "/write");
                    Check(f.Npcs.TryUpdate(f.Actor.Handle, NpcUpdate(f.Actor), out _), mutation + "/restore");
                }
                else if (mutation == "shotABA")
                {
                    Check(f.Shots.TryUpdate(f.Shot.Handle, ShotUpdate(f.Shot, 1), out _), mutation + "/write");
                    Check(f.Shots.TryUpdate(f.Shot.Handle, ShotUpdate(f.Shot), out _), mutation + "/restore");
                }
                else if (mutation == "rng") f.Random.Next();
                else if (mutation == "terrain") f.Tiles.Set(114, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                else if (mutation == "status")
                {
                    Check(f.Status.TryPlanAddition(f.Actor, new BuffTypeId(20), 600, out var external), mutation + "/capture");
                    Check(f.Status.TryAdoptAddition(external, f.Actor), mutation + "/adoptExternalStatusOnly");
                }
                else if (mutation == "ledger")
                {
                    var ledger = (TerraRuntime.Core.Npcs.RuntimeNpcPlayerInteractionLedger)typeof(RuntimeNpcNetworkCombatPipeline)
                        .GetField("interactions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Combat)!;
                    Check(ledger.TryMark(f.Actor.Handle, f.Connection.Player), mutation + "/externalCreditOnly");
                }
                else f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Connection, new(new(0), VanillaPlayerItemSlotCatalog.AmmoSlotStart, 6, 0, 97, 0)));
                var retainedRandom = f.Random.Clone();
                _ = f.Drain();
                Check(!plan!.CanAdopt && !plan.TryAdoptUnpublished(), mutation + "/refuse");
                Check(f.Random.HasSameState(retainedRandom), mutation + "/noRNG");
                var npc = new NpcSnapshot[f.Npcs.Capacity];
                int count = f.Npcs.CopyActive(npc);
                Check(npc.Take(count).All(n => n.Simulation.Life == 1000), mutation + "/noHP");
                Check(f.Drain().Length == 0, mutation + "/noPublication");
            }
        }
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, guardRow))
        {
            VanillaUnifiedRandom1458? externallyChanged = null;
            int callbacks = 0;
            f.BeforeTick = () => { callbacks++; f.Random.Next(); externallyChanged = f.Random.Clone(); };
            Check(!f.Pass.TryPrepareOrdinaryArrowContinuation(f.Shot, new VanillaProjectileWorldStateStepper(f.Tiles), out var rejected, out _), "lateProvider/refuse");
            Check(callbacks == 1 && rejected is null && externallyChanged is not null, "lateProvider/actualProviderRan");
            Check(f.Random.HasSameState(externallyChanged!), "lateProvider/noRewindOrOffers");
            Check(f.Npcs.TryGet(f.Actor.Handle, out var retained) && retained == f.Actor, "lateProvider/noNPCwrites");
            Check(f.Drain().Length == 0, "lateProvider/noPublication");
        }
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, guardRow, life: 1))
        {
            Check(!f.Pass.TryPrepareOrdinaryArrowContinuation(f.Shot, new VanillaProjectileWorldStateStepper(f.Tiles), out var rejected, out _), "lethal/preOffersRefusal");
            Check(rejected is null, "lethal/noPlan");
            RandomEquals(f.Random, guardRow.GetProperty("beforeRandom"), "lethal/noRNG");
        }
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, guardRow))
        {
            Check(f.Pass.TryPrepareOrdinaryArrowContinuation(f.Shot, new VanillaProjectileWorldStateStepper(f.Tiles), out var plan, out _) && plan is not null, "reentry/prepare");
            using (plan!)
            {
                Check(plan!.TryAdoptUnpublished(), "reentry/adopt");
                int callbacks = 0;
                f.OnNpcCommit = () =>
                {
                    callbacks++;
                    plan.Dispose();
                    Check(!plan.TryPublish(), "reentry/nestedPublishRefused");
                    Check(!f.Pass.TryPrepareOrdinaryArrowContinuation(plan.FinalProjectile, new VanillaProjectileWorldStateStepper(f.Tiles), out _, out _), "reentry/scratchStillBusy");
                    RandomEquals(f.Random, guardRow.GetProperty("afterRandom"), "reentry/finalRNG");
                };
                Check(plan.TryPublish(), "reentry/publishConcrete");
                f.OnNpcCommit = null;
                Check(callbacks == 1 && !plan.TryPublish(), "reentry/noRestOrReplay");
            }
        }


        var one = source.RootElement.EnumerateArray().Single(r => r.GetProperty("shotType").GetInt32() == 4 && r.GetProperty("geometry").GetString() == "one-overlap" && r.GetProperty("seed").GetInt32() == 0);

        foreach (string boundary in new[] { "fiveTargets", "zeroPen", "expiry" })
        {
            using var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 4, one);
            if (boundary == "fiveTargets")
            {
                for (byte slot = 1; slot < 5; slot++)
                    Check(f.Npcs.TrySpawn(slot, new NpcStateUpdate(3, 3, f.Actor.PositionX, f.Actor.PositionY, 0, 0, f.Actor.Target, f.Actor.Ai, f.Actor.Simulation), out _), boundary + "/extraTarget");
            }
            else
            {
                Check(f.Shots.TryGetLifecycle(f.Shot.Handle, out var life), boundary + "/life");
                var q = f.Shot;
                var update = new ProjectileStateUpdate(q.Type, q.Spawner, q.PositionX, q.PositionY, q.VelocityX, q.VelocityY, q.Ai, q.BannerIdToRespondTo, q.Damage, q.KnockBack, q.OriginalDamage);
                Check(f.Shots.TryCommitSimulationStepUnpublished(q.Handle, update, boundary == "expiry" ? 1 : life.TimeLeft,
                    null, null, out _, out _, boundary == "zeroPen" ? 0 : null), boundary + "/setup");
            }
            _ = f.Drain();
            Check(f.Shots.TryGet(f.Shot.Handle, out var before), boundary + "/capture");
            Check(!f.Pass.TryPrepareOrdinaryArrowContinuation(before, new VanillaProjectileWorldStateStepper(f.Tiles), out var rejected, out bool owned), boundary + "/initialRefusal");
            Check(rejected is null && !owned, boundary + "/legacyCallerRetainsOwnership");
            Check(f.Shots.TryGet(before.Handle, out var after) && before == after, boundary + "/prepareAtomic");
            RandomEquals(f.Random, one.GetProperty("beforeRandom"), boundary + "/noSpeculativeOffers");
            Check(f.Drain().Length == 0, boundary + "/noSpeculativePublication");

        }

        using (var terminal = Load("terminalProgress"))
        {
            foreach (var row in terminal.RootElement.EnumerateArray())
            {
                using var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, row.GetProperty("shotType").GetInt32(), row);
                var shot = f.Shot;
                var state = new ProjectileStateUpdate(shot.Type, shot.Spawner, shot.PositionX, shot.PositionY, shot.VelocityX, shot.VelocityY,
                    shot.Ai, shot.BannerIdToRespondTo, shot.Damage, shot.KnockBack, shot.OriginalDamage);
                Check(f.Shots.TryCommitSimulationStepUnpublished(shot.Handle, state, 3, null, null, out _, out _), "expiry/configuredTime3");
                var authority = Authority(f);
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    Check(authority.TryTickState(f.Pass), "expiry/actualSuccessiveTick");
                    bool active = f.Shots.TryGet(shot.Handle, out _);
                    Check(active == step.GetProperty("after").GetProperty("active").GetBoolean(), "expiry/sourceLiveness");
                    if (active)
                    {
                        Check(f.Shots.TryGetLifecycle(shot.Handle, out var life) && life.TimeLeft == step.GetProperty("after").GetProperty("timeLeft").GetInt32(), "expiry/sourceTime");
                    }
                    f.Pass.Tick();
                }
                Check(!f.Shots.TryGet(shot.Handle, out _), "expiry/eventuallyRetired");
                Check(!f.ProjectileRegistry.WireIdentities.TryGetWireKey(shot.Handle, out _), "expiry/legacyRemovalUnbinds");
                Check(ReplayBaseline(f).All(frame => frame[2] != 27), "expiry/noRetiredJoinBaseline");
                _ = f.Drain();
                // The terminal tick uses the preserved generic caller. Its visual RNG/motion are not
                // claimed equivalent to the original source-offer continuation reference. Original29
                // remains in the fixture; generic Remove currently clears the baseline without that frame.
            }
        }
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 4, one))
        {
            var wrong = Authority(f, new VanillaUnifiedRandom1458(0));
            bool threw = false;
            try { wrong.TryTickState(f.Pass); } catch (ArgumentException) { threw = true; }
            Check(threw && f.Drain().Length == 0, "mismatch/refuseBinding");
            RandomEquals(f.Random, one.GetProperty("beforeRandom"), "mismatch/noOffers");

        }
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 4, one))
        {
            var custom = new OrdinaryArrowRefusingStepper1458();
            var authority = new ProjectileAuthority(f.Shots, f.Players, f.Npcs, new RuntimePlayerSnapshotLookup(f.Players, null), custom, f.ProjectileRegistry, () => 100,
                worldTiles: f.Tiles, projectileRandom: f.Random, npcReplication: f.NpcRegistry);
            Check(authority.TryTickState(f.Pass) && custom.Calls == 1, "custom/retainedCaller");
            Check(f.Shots.TryGet(f.Shot.Handle, out var after) && after == f.Shot, "custom/noForcedActorRoute");
            RandomEquals(f.Random, one.GetProperty("beforeRandom"), "custom/noOffers");

        }
        var overlap = source.RootElement.EnumerateArray().Single(r => r.GetProperty("shotType").GetInt32() == 5 && r.GetProperty("geometry").GetString() == "two-overlap" && r.GetProperty("seed").GetInt32() == 0);
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, overlap))
        {
            var authority = Authority(f);
            int calls = 0;
            f.OnNpcCommit = () =>
            {
                calls++;
                Check(!authority.TryTickState(f.Pass), "reentry/nestedDriverRefuses");
                var checkpoint = f.Random.Clone();
                f.Pass.Tick();
                Check(f.Random.HasSameState(checkpoint), "reentry/outerPassAlreadyHandled");
            };
            Check(authority.TryTickState(f.Pass) && calls == 2, "reentry/oneJournal");
            Compare(f, overlap, "reentry/final");
            Check(f.Drain().Count(x => x[2] is 27 or 28) == overlap.GetProperty("frames").GetArrayLength(), "reentry/noDuplicateFrames");

        }
        var bindingRow = source.RootElement.EnumerateArray().Single(r => r.GetProperty("shotType").GetInt32() == 5 && r.GetProperty("geometry").GetString() == "one-overlap" && r.GetProperty("seed").GetInt32() == 0);
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, bindingRow))
        {
            var authority = Authority(f);
            ProjectileSnapshot held = default;
            bool rebound = false;
            f.OnNpcCommit = () =>
            {
                f.OnNpcCommit = null;
                Check(f.Shots.TryGet(f.Shot.Handle, out held), "binding/unchangedFinal");
                Check(f.ProjectileRegistry.WireIdentities.TryGetWireKey(held.Handle, out var oldKey), "binding/reverseKey");
                Check(f.Shots.TrySpawn(9, new ProjectileStateUpdate(new(1), 0, 100, 100, 0, 0, default, 0, 1, 0, 0), out var other), "binding/differentActor");
                Check(f.ProjectileRegistry.WireIdentities.TryBind(new(oldKey.Spawner, oldKey.ProjectileIndex, (ushort)(oldKey.Generation + 1)), other.Handle), "binding/forwardShadow");
                rebound = true;
            };
            Check(authority.TryTickState(f.Pass) && rebound, "binding/actualCallback");
            Check(f.Shots.TryGet(f.Shot.Handle, out var final) && final == held, "binding/noOriginalStateChange");
            Check(f.Drain().Count(frame => frame[2] == 27) == 1, "binding/onlyCallbackBirthNoStaleHistory");
            Check(ReplayBaseline(f).Any(frame => frame.SequenceEqual(Encode(held))), "binding/finalBaselineNotRewound");
        }
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, bindingRow))
        {
            var wrong = new PlayerAuthority(null, f.Tiles);
            var authority = new ProjectileAuthority(f.Shots, wrong, f.Npcs, new RuntimePlayerSnapshotLookup(f.Players, null),
                new VanillaProjectileWorldStateStepper(f.Tiles), f.ProjectileRegistry, () => 100,
                worldTiles: f.Tiles, projectileRandom: f.Random, npcReplication: f.NpcRegistry);
            var before = f.Random.Clone();
            Assert.Throws<ArgumentException>(() => authority.TryTickState(f.Pass));
            Check(f.Shots.TryGet(f.Shot.Handle, out var retained) && retained == f.Shot && f.Random.HasSameState(before), "wrongPlayer/noAdoption");
        }
        using (var f = new ArrowWholeUpdateSourceSupport1458.Fixture(0, 5, bindingRow))
        {
            var authority = Authority(f);
            NpcSnapshot replacement = default;
            bool replaced = false;
            f.OnNpcCommit = () =>
            {
                f.OnNpcCommit = null;
                Check(f.Npcs.TryDespawn(f.Actor.Handle), "replacement/retire");
                Check(f.Npcs.TrySpawn(0, new NpcStateUpdate(3, 3, 1818, 1600, 0, 0, 0, default, f.Actor.Simulation), out replacement), "replacement/birth");
                replaced = true;
            };
            Check(authority.TryTickState(f.Pass) && replaced, "replacement/actualCallback");
            long hits = f.Pass.CommittedHits;
            var before = f.Random.Clone();
            f.Pass.Tick();
            Check(f.Npcs.TryGet(replacement.Handle, out var retained) && retained == replacement, "replacement/noOuterHit");
            Check(f.Pass.CommittedHits == hits && f.Random.HasSameState(before), "replacement/noDuplicateCounterOrOffers");
        }

    }
}
internal sealed class OrdinaryArrowRefusingStepper1458 : IProjectileStateStepper
{
    internal int Calls;
    public bool TryStepState(in ProjectileSimulationStepContext projectile, out ProjectileSimulationStepResult next)
    {
        Calls++;
        next = default;
        return false;
    }
}
