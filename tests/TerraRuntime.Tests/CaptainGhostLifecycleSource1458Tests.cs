using System.Text.Json;
using System.Reflection;
using System.IO.Compression;
using Xunit;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Network;
using TerraRuntime.World;


namespace TerraRuntime.Tests;

public sealed class CaptainGhostLifecycleSource1458Tests
{
    [Fact]
    public void Captain_possible_or_unknown_dungeon_spirit_producer_refuses_before_ghost_loot_or_progress()
    {
        foreach (bool unknown in new[] { false, true })
        {
            var fixture = new Fixture(false, false, hardmode: true,
                planteraDowned: unknown ? null : true, dungeon: unknown ? null : true);
            var beforeRandom = fixture.Random.Clone();
            var beforeInvasion = fixture.CaptureInvasion();
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Captain.Handle, 100000));
            Assert.True(fixture.Npcs.TryGet(fixture.Captain.Handle, out var current));
            Assert.Equal(fixture.Captain, current);
            Assert.Equal(1, fixture.Npcs.ActiveCount);
            Assert.True(fixture.Random.HasSameState(beforeRandom));
            Assert.Equal(beforeInvasion, fixture.CaptureInvasion());
            Assert.Empty(fixture.Drops);
            Assert.Empty(fixture.Events);
        }
        var neutralTarget = new Fixture(false, false, hardmode: true, planteraDowned: true, dungeon: false);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            neutralTarget.Pipeline.TryStrikeEnvironment(neutralTarget.Captain.Handle, 100000));
        Assert.Equal(1, neutralTarget.CaptureInvasion().Size);
        Assert.False(neutralTarget.Npcs.TryGet(neutralTarget.Captain.Handle, out _));
    }

    // Configured original checkDead -> UpdateNPC -> UpdateInvasion component evidence.
    // Runtime environment entry contributes its own initial28; source fade negative HP is not owned.
    [Fact]
    public void Captain_death_Ghost_updates_and_completion_replay_the_matching_platform_source()
    {
        using var stream = typeof(CaptainGhostLifecycleSource1458Tests).Assembly.GetManifestResourceStream("CaptainGhostLinkedSource1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int deathCalls = 0, ghostCalls = 0, completionCalls = 0;
        foreach (var row in document.RootElement.GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux").EnumerateArray())
        {
            bool good = row.GetProperty("good").GetBoolean();
            string profile = row.GetProperty("profile").GetString()!;
            var f = new Fixture(good, profile == "full-pool", replicate: true,
                invasionSize: row.GetProperty("size").GetInt32(), playerLiving: profile == "living");
            var peer = f.Join(1);
            QueueCapture.Drain(peer);
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 100000));
            deathCalls++;
            Assert.False(f.Npcs.TryGet(f.Captain.Handle, out _));
            var death = row.GetProperty("stages")[0];
            CompareRandom(death.GetProperty("after"), f.Random);
            CompareWorld(death.GetProperty("after").GetProperty("world"), f.CaptureInvasion());
            CompareDrops(death.GetProperty("after").GetProperty("drops"), f.Drops);
            var birthFrames = QueueCapture.Drain(peer).Where(x => Convert.FromHexString(x)[2] == 23 &&
                Convert.FromHexString(x)[3] == 1).ToArray();
            Assert.Equal(death.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!)
                .Where(x => Convert.FromHexString(x)[2] == 23).ToArray(), birthFrames);
            var handle = new NpcHandle(1, new NpcGeneration(1));
            if (profile != "full-pool")
            {
                Assert.True(f.Npcs.TryGet(handle, out var ghost));
                if (profile == "fade250")
                {
                    var input = new NpcStateUpdate(ghost.Type, ghost.NetId, ghost.PositionX, ghost.PositionY,
                        ghost.VelocityX, ghost.VelocityY, ghost.Target, ghost.Ai, ghost.Simulation with { Alpha = 250 });
                    Assert.True(f.Npcs.TryUpdate(handle, in input, out ghost));
                }
                var targeting = new VanillaNpcTargetingAiStepper(new NoopStepper());
                var raw = new SourceInactivePlayer(profile == "living");
                targeting.SetRawPlayerSlots(raw);
                var motion = new VanillaNpcWorldMotionAiStepper(targeting, new WorldTileStore(new(200, 100)));
                var players = profile == "living" ? new[] { new VanillaNpcTargetCandidate(0, 1010, 461, 0, true, false, false, false) } : Array.Empty<VanillaNpcTargetCandidate>();
                var outer = new RuntimePirateGhostOuterAiStepper1458(new NoopStepper(), f.Npcs, targeting,
                    motion, f.Pipeline, new RuntimeNpcBuffStatus1458(f.Npcs), raw, () => players);
                foreach (var phase in row.GetProperty("stages").EnumerateArray().Where(x => x.GetProperty("phase").GetString() == "UpdateNPC"))
                {
                    QueueCapture.Drain(peer);
                    Assert.Equal(1, new RuntimeNpcAiStateExecutor(f.Npcs).Tick(outer).Applied);
                    ghostCalls++;
                    CompareRandom(phase.GetProperty("after"), f.Random);
                    CompareWorld(phase.GetProperty("after").GetProperty("world"), f.CaptureInvasion());
                    bool live = f.Npcs.TryGet(handle, out var current);
                    var expected = phase.GetProperty("actor");
                    Assert.Equal(expected.GetProperty("state").GetProperty("active").GetBoolean(), live);
                    if (!live) current = f.Commits.Last(x => x.Kind == NpcStateCommitKind.Despawn && x.Npc.Type == 662).Npc;
                    CompareGhost(expected, current);
                    Assert.Equal(phase.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!)
                        .Where(x => Convert.FromHexString(x)[2] is 23 or 28).ToArray(), QueueCapture.Drain(peer));
                }
            }
            var completion = row.GetProperty("stages").EnumerateArray().Last();
            Assert.True(f.Owner.TryCapture(out var before));
            Assert.True(VanillaInvasionLifecycle1458.TryAdvance(before.State, 50, 1, out var transition));
            Assert.True(f.Owner.TryAdopt(in before, in transition, out var accepted));
            completionCalls++;
            CompareWorld(completion.GetProperty("after").GetProperty("world"), accepted.State);
            CompareRandom(completion.GetProperty("after"), f.Random);
            bool completed = accepted.State.Type == 0;
            Assert.Equal(completed ? 11 : 0, transition.Effects.ProgressionEvent);
            Assert.Equal(completed, transition.Effects.WorldInfo);
            Assert.Equal(completed, accepted.State.DownedPirate);
            Assert.Equal(completed, accepted.State.LanternNextNight);
            // Exact packet98 is the existing completion owner input; packet7 uses its own world fixture.
            if (completed) Assert.Equal(Convert.FromHexString(completion.GetProperty("frames")[0].GetString()!),
                TerrariaProgressionEventCodec1458.Encode(11));
        }
        Assert.Equal(16, deathCalls);
        Assert.Equal(20, ghostCalls);
        Assert.Equal(16, completionCalls);
    }

    private static void CompareRandom(JsonElement after, VanillaUnifiedRandom1458 random)
    {
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(RngCapture.Capture(random)));
        Assert.Equal(after.GetProperty("rng").GetProperty("inext").GetUInt32(), actual.RootElement.GetProperty("inext").GetUInt32());
        Assert.Equal(after.GetProperty("rng").GetProperty("SeedArray").EnumerateArray().Select(x => x.GetInt32()),
            actual.RootElement.GetProperty("SeedArray").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(after.GetProperty("next").GetInt32(), random.Clone().Next());
    }
    private static void CompareWorld(JsonElement expected, InvasionState1458 actual)
    {
        Assert.Equal(expected.GetProperty("type").GetInt32(), actual.Type);
        Assert.Equal(expected.GetProperty("size").GetInt32(), actual.Size);
        Assert.Equal(expected.GetProperty("start").GetInt32(), actual.SizeStart);
        Assert.Equal(expected.GetProperty("delay").GetInt32(), actual.Delay);
        Assert.Equal(expected.GetProperty("x").GetDouble(), actual.X);
        Assert.Equal(expected.GetProperty("warning").GetInt32(), actual.Warning);
        Assert.Equal(expected.GetProperty("progress").GetInt32(), actual.Progress);
        Assert.Equal(expected.GetProperty("maximum").GetInt32(), actual.ProgressMax);
        Assert.Equal(expected.GetProperty("downed").GetBoolean(), actual.DownedPirate);
        Assert.Equal(expected.GetProperty("lantern").GetBoolean(), actual.LanternNextNight);
    }
    private static void Float(JsonElement value, float actual) => Assert.Equal(BitConverter.SingleToInt32Bits(value.GetSingle()), BitConverter.SingleToInt32Bits(actual));
    private static void CompareDrops(JsonElement expected, List<WorldItemSnapshot> actual)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            var source = expected[i]; var item = actual[i];
            Assert.Equal(source.GetProperty("type").GetInt32(), item.ItemNetId);
            Assert.Equal(source.GetProperty("stack").GetInt32(), item.Stack);
            Assert.Equal(source.GetProperty("prefix").GetByte(), item.Prefix);
            Float(source.GetProperty("x"), item.PositionX); Float(source.GetProperty("y"), item.PositionY);
            Float(source.GetProperty("vx"), item.VelocityX); Float(source.GetProperty("vy"), item.VelocityY);
        }
    }
    private static void CompareGhost(JsonElement expected, NpcSnapshot actual)
    {
        var state = expected.GetProperty("state"); var sim = actual.Simulation;
        Float(state.GetProperty("x"), actual.PositionX); Float(state.GetProperty("y"), actual.PositionY);
        Float(state.GetProperty("vx"), actual.VelocityX); Float(state.GetProperty("vy"), actual.VelocityY);
        Assert.Equal(state.GetProperty("target").GetInt32(), actual.Target);
        Assert.Equal(state.GetProperty("direction").GetInt32(), sim.DirectionX);
        Assert.Equal(state.GetProperty("spriteDirection").GetInt32(), sim.SpriteDirection);
        Assert.Equal(state.GetProperty("timeLeft").GetInt32(), sim.TimeLeft);
        Assert.Equal(Math.Max(0, state.GetProperty("life").GetInt32()), sim.Life);
        Assert.Equal(state.GetProperty("lifeMax").GetInt32(), sim.LifeMax);
        Assert.Equal(state.GetProperty("justHit").GetBoolean(), sim.JustHit);
        Float(state.GetProperty("knockBackResist"), sim.KnockBackResist!.Value);
        Float(state.GetProperty("scale"), sim.Scale);
        Assert.Equal(expected.GetProperty("alpha").GetInt32(), sim.Alpha);
        Assert.Equal(expected.GetProperty("noGravity").GetBoolean(), sim.NoGravity);
        Assert.Equal(expected.GetProperty("noTileCollide").GetBoolean(), sim.NoTileCollide);
        Float(state.GetProperty("ai")[0], actual.Ai.Ai0); Float(state.GetProperty("ai")[1], actual.Ai.Ai1);
        Float(state.GetProperty("ai")[2], actual.Ai.Ai2); Float(state.GetProperty("ai")[3], actual.Ai.Ai3);
        Float(state.GetProperty("localAI")[0], sim.LocalAi.Ai0); Float(state.GetProperty("localAI")[1], sim.LocalAi.Ai1);
        Float(state.GetProperty("localAI")[2], sim.LocalAi.Ai2); Float(state.GetProperty("localAI")[3], sim.LocalAi.Ai3);
    }

    [Fact]
    public void Coupled_publication_currentness_nested_deaths_and_throw_tails_preserve_owned_state()
    {
        foreach (string mode in new[] { "observer-join", "endpoint-replace", "occupation-replace", "child-change", "throw", "nested-captain", "nested-throw" })
        {
            var fixture = new Fixture(false, false, replicate: true);
            var oldPeer = fixture.Join(1);
            TerrariaConnectionOutboundQueue? newPeer = null;

            NpcSnapshot second = default;
            if (mode is "nested-captain" or "nested-throw")
            {
                var update = new NpcStateUpdate(216, 216, 900, 440, 0, 0, 0, default, NpcSimulationState.Initial);
                if (!fixture.Npcs.TrySpawnVanilla(in update, out second, startSlot: 2)) Assert.Fail("Second Captain setup failed.");
            }
            QueueCapture.Drain(oldPeer);
            VanillaUnifiedRandom1458? nestedCheckpoint = null;
            fixture.OnGhostSpawn = born =>
            {
                if (mode == "observer-join") newPeer = fixture.Join(2);
                if (mode == "endpoint-replace")
                {
                    fixture.Registry.TryUnregister(GameCommandSourceId.FromConnection(1));
                    newPeer = fixture.Join(1);
                }
                if (mode == "occupation-replace")
                {
                    newPeer = oldPeer;
                    fixture.Registry.PlayerSpawned(Fixture.Connection(1), new(new PlayerSlotId(1), 1, 1, 0, 0, 0, 0, 0));
                }
                if (mode == "child-change")
                {
                    var update = new NpcStateUpdate(born.Type, born.NetId, born.PositionX + 1, born.PositionY,
                        born.VelocityX, born.VelocityY, born.Target, born.Ai, born.Simulation);
                    if (!fixture.Npcs.TryUpdate(born.Handle, in update, out _)) Assert.Fail("Mutation setup failed.");
                }
                if (mode == "throw") throw new InvalidOperationException("Observer witness.");
                if (mode is "nested-captain" or "nested-throw")
                {
                    fixture.OnGhostSpawn = null;
                    Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, fixture.Pipeline.TryStrikeEnvironment(second.Handle, 100000));
                    nestedCheckpoint = fixture.Random.Clone();
                    if (mode == "nested-throw") throw new InvalidOperationException("Observer witness.");
                }
            };
            bool thrown = false;
            try { fixture.Pipeline.TryStrikeEnvironment(fixture.Captain.Handle, 100000); }
            catch (Exception e)
        {
            Assert.IsType<InvalidOperationException>(e);
            Assert.Equal("Observer witness.", e.Message);
            thrown = true;
        }
            Assert.False(fixture.Npcs.TryGet(fixture.Captain.Handle, out _));
            Assert.Equal(mode is "nested-captain" or "nested-throw" ? 6 : 3, fixture.Drops.Count);
            if (mode is "nested-captain" or "nested-throw") Assert.False(fixture.Npcs.TryGet(second.Handle, out _));
            if (mode is "observer-join" or "endpoint-replace")
            {
                Assert.NotNull(newPeer);
                Assert.Single(QueueCapture.Drain(newPeer), x => Convert.FromHexString(x)[2] == 23 && Convert.FromHexString(x)[3] == 1);
            }
            bool nested = mode is "nested-captain" or "nested-throw";
            Assert.Equal(mode is "throw" or "nested-throw", thrown);
            Assert.Equal(nested ? 0 : 1, fixture.CaptureInvasion().Size);
            Assert.Equal(nested ? 1875600503 : 569863998, fixture.Random.Clone().Next());
            Assert.True(fixture.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out var retainedGhost));
            Assert.Equal(mode == "child-change" ? 792f : 791f, retainedGhost.PositionX);
            Assert.Equal(nested ? 2 : 1, fixture.Registry.CaptureWorldTransferDespawnFrames().Length);
            if (nested)
            {
                Assert.True(fixture.Npcs.TryGet(new NpcHandle(3, new NpcGeneration(1)), out var secondGhost));
                Assert.Equal(891f, secondGhost.PositionX);
                Assert.NotNull(nestedCheckpoint);
                Assert.True(fixture.Random.HasSameState(nestedCheckpoint));
                Assert.Equal(new[] { "78:0" }, fixture.Events.Where(x => x.StartsWith("78:")));
            }
            var retainedFrames = QueueCapture.Drain(oldPeer);
            var ghostFrames = retainedFrames.Where(x => Convert.FromHexString(x)[2] == 23 && Convert.FromHexString(x)[3] is 1 or 3).ToArray();
            Assert.Equal(mode == "endpoint-replace" ? 0 : nested ? 2 : 1, ghostFrames.Length);
            if (mode == "child-change") Assert.Equal(792f, BitConverter.ToSingle(Convert.FromHexString(Assert.Single(ghostFrames)), 5));

        }


        foreach (bool attached in new[] { false, true })
            foreach (bool good in new[] { false, true })
            {
                var f = new Fixture(good, false, invasionType: 0, attachInvasion: attached);
                var accepted = f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 100000);
                Assert.False(f.Npcs.TryGet(f.Captain.Handle, out _));
                Assert.True(f.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out var ghost));
                Assert.Equal(6, f.CaptureInvasion().Size);
                Assert.DoesNotContain(f.Events, e => e.StartsWith("78:"));
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, accepted);
                Assert.Equal(0, f.CaptureInvasion().Type);
                Assert.Equal(good ? 2039662424 : 569863998, f.Random.Clone().Next());
                using (var resource = typeof(CaptainGhostLifecycleSource1458Tests).Assembly.GetManifestResourceStream("CaptainGhostLinkedSource1458")!)
                using (var gzip = new GZipStream(resource, CompressionMode.Decompress))
                using (var source = JsonDocument.Parse(gzip))
                {
                    var reference = source.RootElement.GetProperty("outside").EnumerateArray().Single(x =>
                        x.GetProperty("good").GetBoolean() == good && x.GetProperty("profile").GetString() == "living");
                    var after = reference.GetProperty("stages")[0].GetProperty("after");
                    CompareRandom(after, f.Random);
                    CompareWorld(after.GetProperty("world"), f.CaptureInvasion());
                    CompareDrops(after.GetProperty("drops"), f.Drops);
                }
                Assert.Equal(662, ghost.Type);
                Assert.Equal(791f, ghost.PositionX);
                Assert.Equal(400f, ghost.PositionY);

            }


        foreach (string mode in new[] { "projectile", "projectile-nested", "projectile-nested-throw" })
        {
            var f = new Fixture(false, false, replicate: true);
            NpcSnapshot second = default;
            if (mode != "projectile")
            {
                var body = new NpcStateUpdate(216, 216, 900, 440, 0, 0, 0, default, NpcSimulationState.Initial);
                if (!f.Npcs.TrySpawnVanilla(in body, out second, startSlot: 2)) Assert.Fail("Second setup.");
            }
            var before = f.Random.Clone();
            var after = before.Clone();
            for (int offer = 0; offer < 4; offer++) _ = after.Next();

            VanillaUnifiedRandom1458? acceptedCheckpoint = null;
            bool threw = false;
            var shot = new ProjectileSnapshot(new(0, new ProjectileGeneration(1)), new(1), new(14), 0,
                800, 440, 1, 0, default, 0, 32767, 0, 32767);
            string? result = null;
            try
            {
                result = f.Pipeline.TryStrikePreparedProjectile(in shot, in f.Captain, 0, 100000, 0, false,
                    before, after, () => true, committed =>
                    {
                        if (f.Random.HasSameState(after)) Assert.Fail("Death cursor rewound to predeath projectile offers.");

                        if (mode != "projectile") f.Pipeline.TryStrikeEnvironment(second.Handle, 100000);
                        acceptedCheckpoint = f.Random.Clone();
                        if (mode == "projectile-nested-throw") throw new InvalidOperationException("Nested prelude witness.");
                    }).ToString();
            }
            catch (InvalidOperationException e) when (e.Message == "Nested prelude witness.") { threw = true; }
            if (f.Npcs.TryGet(f.Captain.Handle, out _) || f.Drops.Count != (mode == "projectile" ? 4 : 8) ||
                mode != "projectile" && f.Npcs.TryGet(second.Handle, out _)) Assert.Fail("Projectile death drain incomplete.");
            if (acceptedCheckpoint is null || !f.Random.HasSameState(acceptedCheckpoint)) Assert.Fail("Publication rewound accepted nested cursor.");
            Assert.Equal(mode == "projectile-nested-throw", threw);
            Assert.Equal(mode == "projectile" ? 1 : 0, f.CaptureInvasion().Size);
            Assert.True(f.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out _));
            if (mode != "projectile") Assert.True(f.Npcs.TryGet(new NpcHandle(3, new NpcGeneration(1)), out _));

        }
        var lateRaw = new Fixture(false, false, playerLiving: false);
        lateRaw.Pipeline.TryStrikeEnvironment(lateRaw.Captain.Handle, 100000);
        lateRaw.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out var rawGhost);
        var rawOwner = new SourceInactivePlayer();
        var rawTargeting = new VanillaNpcTargetingAiStepper(new NoopStepper());
        rawTargeting.SetRawPlayerSlots(rawOwner);
        var rawMotion = new VanillaNpcWorldMotionAiStepper(rawTargeting, new WorldTileStore(new(200, 100)));
        int captures = 0;
        var rawOuter = new RuntimePirateGhostOuterAiStepper1458(new NoopStepper(), lateRaw.Npcs, rawTargeting,
            rawMotion, lateRaw.Pipeline, new RuntimeNpcBuffStatus1458(lateRaw.Npcs), rawOwner, () =>
            { if (++captures == 2) rawOwner.X = 1100; return Array.Empty<VanillaNpcTargetCandidate>(); });
        var rawBefore = lateRaw.Random.Clone();
        var rawSummary = new RuntimeNpcAiStateExecutor(lateRaw.Npcs).Tick(rawOuter);
        if (rawSummary.Applied != 0 || !lateRaw.Npcs.TryGet(rawGhost.Handle, out var rawAfter) || rawAfter != rawGhost ||
            !rawBefore.HasSameState(lateRaw.Random) || captures != 2) Assert.Fail("Late raw-slot provider did not refuse.");

        var providerJoin = new Fixture(false, false, replicate: true);
        var providerPeer = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(2048, 1048576, 16384));
        Assert.True(providerJoin.Registry.TryRegister(GameCommandSourceId.FromConnection(2), providerPeer));
        bool joinedBeforeAdoption = false;
        providerJoin.OnPlayerRead = () =>
        {
            // An external player lookup joins before the first owner write; its peer receives one birth.
            if (joinedBeforeAdoption) return;
            joinedBeforeAdoption = true;
            Assert.True(providerJoin.Npcs.TryGet(providerJoin.Captain.Handle, out _));
            providerJoin.Registry.PlayerSpawned(Fixture.Connection(2), new(new PlayerSlotId(2), 1, 1, 0, 0, 0, 0, 0));
            Assert.DoesNotContain(QueueCapture.Drain(providerPeer), x => Convert.FromHexString(x)[2] == 23 && Convert.FromHexString(x)[3] == 1);
        };
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, providerJoin.Pipeline.TryStrikeEnvironment(providerJoin.Captain.Handle, 100000));
        Assert.True(joinedBeforeAdoption);
        Assert.Single(QueueCapture.Drain(providerPeer), x => Convert.FromHexString(x)[2] == 23 && Convert.FromHexString(x)[3] == 1);

        // Completion is not gated on a living Ghost: Captain's final five credits complete immediately.
        var immediate = new Fixture(false, false, invasionSize: 5);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, immediate.Pipeline.TryStrikeEnvironment(immediate.Captain.Handle, 100000));
        Assert.True(immediate.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out _));
        Assert.True(immediate.Owner.TryCapture(out var completionBefore));
        Assert.True(VanillaInvasionLifecycle1458.TryAdvance(completionBefore.State, 50, 1, out var complete));
        Assert.True(immediate.Owner.TryAdopt(in completionBefore, in complete, out var completeAccepted));
        Assert.True(completeAccepted.State.DownedPirate);
        Assert.True(completeAccepted.State.LanternNextNight);
        Assert.True(immediate.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out _));

        foreach (string mutation in new[] { "progression", "spawn-context", "clock", "daily" })
        {
            var f = new Fixture(false, false, playerLiving: false, slimeRain: mutation == "clock");
            f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 100000);
            Assert.True(f.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out var ghost));
            var alpha = new NpcStateUpdate(ghost.Type, ghost.NetId, ghost.PositionX, ghost.PositionY,
                ghost.VelocityX, ghost.VelocityY, ghost.Target, ghost.Ai, ghost.Simulation with { Alpha = 250 });
            Assert.True(f.Npcs.TryUpdate(ghost.Handle, in alpha, out ghost));
            var beforeRandom = f.Random.Clone();
            int beforeEvents = f.Events.Count, beforeDrops = f.Drops.Count, providerCalls = 0;
            var raw = new SourceInactivePlayer();
            var targeting = new VanillaNpcTargetingAiStepper(new NoopStepper());
            targeting.SetRawPlayerSlots(raw);
            var motion = new VanillaNpcWorldMotionAiStepper(targeting, new WorldTileStore(new(200, 100)));
            var outer = new RuntimePirateGhostOuterAiStepper1458(new NoopStepper(), f.Npcs, targeting,
                motion, f.Pipeline, new RuntimeNpcBuffStatus1458(f.Npcs), raw, () =>
                {
                    if (++providerCalls == 2)
                    {
                        if (mutation == "progression") f.Progression.MarkCompleted(VanillaWorldProgressionId.GoblinArmy);
                        else if (mutation == "spawn-context") f.Npcs.SetVanillaSpawnContextSource(() => new(2f, 1, true));
                        else if (mutation == "clock") f.Clock.TryAdvanceSlimeRainKillCount(true, false, false);
                        else f.Clock.BossRecoveryDailyState.Record(VanillaNpcIds.EyeOfCthulhu);
                    }
                    return Array.Empty<VanillaNpcTargetCandidate>();
                });
            int applied = -1;
            Assert.Null(Record.Exception(() => applied = new RuntimeNpcAiStateExecutor(f.Npcs).Tick(outer).Applied));
            Assert.Equal(0, applied);
            Assert.Equal(2, providerCalls);
            Assert.True(f.Npcs.TryGet(ghost.Handle, out var after));
            Assert.Equal(ghost, after);
            Assert.True(beforeRandom.HasSameState(f.Random));
            Assert.Equal(beforeEvents, f.Events.Count);
            Assert.Equal(beforeDrops, f.Drops.Count);
            if (mutation == "clock") Assert.Equal(1, f.Clock.SlimeRainKillCount);
            if (mutation == "daily") Assert.True(f.Clock.BossRecoveryDailyState.EyeKilled);
        }

        var lateDeathRaw = new Fixture(false, false, playerLiving: false);
        lateDeathRaw.Pipeline.TryStrikeEnvironment(lateDeathRaw.Captain.Handle, 100000);
        Assert.True(lateDeathRaw.Npcs.TryGet(new NpcHandle(1, new NpcGeneration(1)), out var lateGhost));
        var lateAlpha = new NpcStateUpdate(lateGhost.Type, lateGhost.NetId, lateGhost.PositionX, lateGhost.PositionY,
            lateGhost.VelocityX, lateGhost.VelocityY, lateGhost.Target, lateGhost.Ai, lateGhost.Simulation with { Alpha = 250 });
        Assert.True(lateDeathRaw.Npcs.TryUpdate(lateGhost.Handle, in lateAlpha, out lateGhost));
        var lateRawOwner = new SourceInactivePlayer();
        int lateCaptures = 0;
        lateDeathRaw.Npcs.SetVanillaSpawnContextSource(() =>
        {
            if (lateCaptures >= 2) lateRawOwner.X = 1100;
            return new(1f, 1, false);
        });
        var lateTargeting = new VanillaNpcTargetingAiStepper(new NoopStepper());
        lateTargeting.SetRawPlayerSlots(lateRawOwner);
        var lateMotion = new VanillaNpcWorldMotionAiStepper(lateTargeting, new WorldTileStore(new(200, 100)));
        var lateOuter = new RuntimePirateGhostOuterAiStepper1458(new NoopStepper(), lateDeathRaw.Npcs, lateTargeting,
            lateMotion, lateDeathRaw.Pipeline, new RuntimeNpcBuffStatus1458(lateDeathRaw.Npcs), lateRawOwner,
            () => { lateCaptures++; return Array.Empty<VanillaNpcTargetCandidate>(); });
        var lateRandom = lateDeathRaw.Random.Clone();
        int lateDrops = lateDeathRaw.Drops.Count, lateEvents = lateDeathRaw.Events.Count;
        Assert.Equal(0, new RuntimeNpcAiStateExecutor(lateDeathRaw.Npcs).Tick(lateOuter).Applied);
        Assert.Equal(1100f, lateRawOwner.X);
        Assert.True(lateDeathRaw.Npcs.TryGet(lateGhost.Handle, out var lateAfter));
        Assert.Equal(lateGhost, lateAfter);
        Assert.True(lateRandom.HasSameState(lateDeathRaw.Random));
        Assert.Equal(lateDrops, lateDeathRaw.Drops.Count);
        Assert.Equal(lateEvents, lateDeathRaw.Events.Count);

        // Actual State.Tick selected birth: runtime integration, not a whole original spawner claim.
        var naturalStore = new RuntimeNpcStore();
        var naturalWorld = new WorldTileStore(new WorldDimensions(400, 220));
        for (int x = 0; x < 400; x++) naturalWorld.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var naturalRandom = new SystemVanillaNpcRandom(35);
        var naturalInvasion = new RuntimeWorldInvasion1458(default(InvasionState1458) with { Type = 3, Size = 120, SizeStart = 120, X = 200 });
        var naturalState = new ServerRuntimeState(npcs: naturalStore, npcAiStepper: new NoopStepper(), worldTiles: naturalWorld,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 120, RockLayer = 160, SpawnTileY = 100 },
            invasion: naturalInvasion, naturalSpawnRandom: naturalRandom, invasionProgressPublisher: _ => { });
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9912), session.Handle);
        naturalState.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 60, 0, 0, 0, 0, 0)));
        naturalState.Tick();
        Assert.Equal(1, naturalState.AppliedNpcSpawns);
        Assert.True(naturalStore.TryGetActive(0, out var naturalCaptain));
        Assert.Equal(216, naturalCaptain.Type);
        Assert.Equal(2079f, naturalCaptain.PositionX);
        Assert.Equal(1560f, naturalCaptain.PositionY);
        Assert.Equal(312046615, naturalRandom.SourceRandom.Clone().Next());

        // Existing allocator's source-selected slot is not changed by an unrelated tail fence.
        foreach (string mode in new[] { "later-protected", "later-saturated", "chosen-saturated", "replace-before-free", "protected-pool" })
        {
            var f = new Fixture(true, false);
            if (mode == "later-protected") SetSlotField(f.Npcs, 2, "SpawnProtection", 1);
            if (mode == "later-saturated") SetSlotField(f.Npcs, 2, "Generation", ulong.MaxValue);
            if (mode == "chosen-saturated") SetSlotField(f.Npcs, 1, "Generation", ulong.MaxValue);
            if (mode == "protected-pool")
                for (int slot = 1; slot < 200; slot++) SetSlotField(f.Npcs, slot, "SpawnProtection", 1);
            if (mode == "replace-before-free")
            {
                var replace = new NpcStateUpdate(3, 3, 900, 440, 0, 0, 0, default,
                    NpcSimulationState.Initial with { CanBeReplacedByOtherNpcs = true });
                Assert.True(f.Npcs.TrySpawn(1, in replace, out _));
            }
            var beforeRandom = f.Random.Clone();
            var result = f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 100000);
            if (mode == "chosen-saturated")
            {
                Assert.NotEqual(RuntimeTownNpcMeleeDamageResult1458.Killed, result);
                Assert.True(f.Npcs.TryGet(f.Captain.Handle, out var parent));
                Assert.Equal(f.Captain, parent);
                Assert.True(beforeRandom.HasSameState(f.Random));
                Assert.Empty(f.Drops);
            }
            else
            {
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, result);
                Assert.False(f.Npcs.TryGet(f.Captain.Handle, out _));
                Assert.Equal(mode != "protected-pool", f.Npcs.TryGet(new NpcHandle((byte)(mode == "replace-before-free" ? 2 : 1), new NpcGeneration(1)), out var child));
                if (mode != "protected-pool") Assert.Equal(662, child.Type);
                Assert.Equal(2039662424, f.Random.Clone().Next());
            }
        }

    }

    private static void SetSlotField(RuntimeNpcStore npcs, int index, string field, object value)
    {
        var slots = (Array)typeof(RuntimeNpcStore).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(npcs)!;
        object state = slots.GetValue(index)!;
        state.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public)!.SetValue(state, value);
        slots.SetValue(state, index);
    }

    sealed class NoopStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
    sealed class SourceInactivePlayer(bool active = false) : INpcRawPlayerSlotLookup1458
    {
        internal float X = 1000;
        public bool TryCapture(byte slot, out NpcRawPlayerSlotSnapshot1458 snapshot)
        { snapshot = new(new(slot, active, false, false, X, 440, 20, 42, 0, false, 0), 1, 1, null); return slot == 0; }
        public bool IsCurrent(in NpcRawPlayerSlotSnapshot1458 snapshot) => TryCapture(snapshot.Facts.Slot, out var current) && current == snapshot;
    }

    sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly RuntimeWorldInvasion1458 Owner;
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly NpcSnapshot Captain;
        internal readonly List<string> Events = [];
        internal readonly List<WorldItemSnapshot> Drops = [];
        internal sealed record Commit(NpcStateCommitKind Kind, NpcSnapshot Npc);
        internal readonly List<Commit> Commits = [];
        internal readonly RuntimeNpcReplicationRegistry Registry = new();
        internal readonly RuntimeWorldProgressionMutations Progression = new();
        internal readonly RuntimeWorldClock Clock;
        internal Action<NpcSnapshot>? OnGhostSpawn;
        internal Action? OnPlayerRead;
        private readonly PlayerStateSnapshot player;
        private readonly bool living;

        internal Fixture(bool good, bool full, bool replicate = false, int invasionType = 3, bool attachInvasion = true, int invasionSize = 6, bool playerLiving = true, bool slimeRain = false,
            bool hardmode = false, bool? planteraDowned = null, bool? dungeon = false)
        {
            living = playerLiving;
            Clock = new(0, true, 0, slimeRain ? 100 : 0, 0);
            Owner = new(new InvasionState1458(invasionType, invasionSize, 120, 0, 50, 3600, 0, 0, 0, 0,
                false, false, false, false));
            Npcs = new(200, replicate ? new RuntimeNpcStateCommitFanout(this, Registry) : this);
            Npcs.SetVanillaSpawnContextSource(() => new(good ? 2f : 1f, 1, good));
            var items = new RuntimeWorldItemStore(this);
            player = default(PlayerStateSnapshot) with
            {
                Player = new(new PlayerSlotId(0), new PlayerSessionGeneration(1)),
                Revision = new(1),
                PositionX = 1000,
                PositionY = 440,
                Zones = dungeon is { } knownDungeon ? new((byte)(knownDungeon ? 1 : 0), 0, 0, 0, 0, 0) : null,
                HasHealth = true,
                Life = 400,
                MaxLife = 400,
                DerivedLifeMax = 400,
                HasMana = true,
                Mana = 200,
                MaxMana = 200,
                NpcLifeCurrent = true
            };
            var update = new NpcStateUpdate(216, 216, 800, 440, 0, 0, 0, default, NpcSimulationState.Initial);
            if (!Npcs.TrySpawnVanilla(in update, out Captain)) Assert.Fail("Captain creation refused.");
            if (full)
            {
                var zombie = update with { Type = 3, NetId = 3 };
                for (byte slot = 1; slot < 200; slot++)
                    if (!Npcs.TrySpawn(slot, in zombie, out _)) Assert.Fail("Full pool setup refused.");
            }
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            Pipeline = new(Npcs, items, this, new PlayerAuthority(null, null), () => 0,
                replicate ? Registry : null, new(items), null, Clock, Progression, false, false,
                lootRandom: Random, lootRemixWorld: false,
                planteraDownedBaseline: planteraDowned,
                globalLootWorld: new(100, 80, 1, hardmode, false, false, false, 35, 40, false, false),
                npcSpecificGoodWorld: good,
                invasion: attachInvasion ? Owner : null, invasionProgressPublisher: accepted => Events.Add("78:" + accepted.State.Size));
            Events.Clear();
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        { OnPlayerRead?.Invoke(); snapshot = player; return slot.Value == 0 && living; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        {
            Commits.Add(new(kind, npc));
            Events.Add("23:" + kind + ":" + npc.Handle.Slot + ":" + npc.TypeIdentity.Value);
            if (kind == NpcStateCommitKind.Spawn && npc.TypeIdentity == VanillaNpcIds.PirateGhost)
                OnGhostSpawn?.Invoke(npc);
        }
        internal InvasionState1458 CaptureInvasion()
        { Owner.TryCapture(out var capture); return capture.State; }
        internal static ConnectionHandle Connection(int source) => new(GameCommandSourceId.FromConnection(source),
            new(new PlayerSlotId((byte)source), new PlayerSessionGeneration(1)));
        internal TerrariaConnectionOutboundQueue Join(int source)
        {
            var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(2048, 1048576, 16384));
            if (!Registry.TryRegister(GameCommandSourceId.FromConnection(source), queue)) Assert.Fail("Join failed.");
            var connection = Connection(source);
            Registry.PlayerSpawned(connection, new(connection.Player.Slot, 1, 1, 0, 0, 0, 0, 0));
            return queue;
        }
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            if (kind != WorldItemStateCommitKind.Drop) return;
            Drops.Add(item); Events.Add("21:" + item.ItemNetId + ":" + item.Stack);
        }
    }

    static class RngCapture
    {
        internal static object Capture(VanillaUnifiedRandom1458 random) => new
        {
            inext = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(random)!,
            SeedArray = ((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(random)!).ToArray()
        };
    }

    static class QueueCapture
    {
        internal static string[] Drain(TerrariaConnectionOutboundQueue outbound)
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var frames = new List<string>();
            while (queue.TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
            return frames.ToArray();
        }
    }

}
