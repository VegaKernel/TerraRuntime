using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcUndeadVariantsRuntime1458Tests
{
    // Explicit identities, rather than an aiStyle-wide lifecycle admission. Giant Skeleton (-15),
    // Bone Throwing Skeletons and Torch Zombie have additional source branches and stay separate.
    private static readonly int[] Identities =
    [
        -13, -14, -26, -27, -28, -29, -30, -31, -32, -33, -34, -35, -36, -37,
        -44, -45, -46, -47, -48, -49, -50, -51, -52, -53, -54, -55, 201, 202, 203, 635
    ];

    [Fact]
    public void Whole_source_birth_updates_interaction_strikes_and_drops_share_the_existing_owners()
    {
        using var stream = typeof(NpcUndeadVariantsRuntime1458Tests).Assembly.GetManifestResourceStream(
            OperatingSystem.IsWindows() ? "UndeadVariantsRuntimeWindows1458" : "UndeadVariantsRuntimeLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var missingFrames = new List<string>();
        int cases = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            int requested = row.GetProperty("requested").GetInt32();
            if (!Identities.Contains(requested)) continue;
            string context = $"{requested}/{row.GetProperty("name").GetString()}/surface{row.GetProperty("worldSurface")}";
            using var fixture = new Fixture(row);
            AssertState(row.GetProperty("birth"), fixture.Born, fixture.Random, context + "/birth");
            foreach (var phase in row.GetProperty("phases").EnumerateArray())
            {
                Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), fixture.Tick());
                Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var actual));
                AssertState(phase.GetProperty("state"), actual, fixture.Random, context + "/tick" + phase.GetProperty("tick"));
                AssertSourceFrames(phase.GetProperty("frames"), fixture.Live, context + "/tick" + phase.GetProperty("tick"), missingFrames);
            }
            var late = AssertLateJoin(fixture, row.GetProperty("phases")[7].GetProperty("state"));

            // NPC.PlayerInteraction is a distinct source call before StrikeNPC in the original trace.
            Assert.True(fixture.Combat.Interactions.TryMark(fixture.Born.Handle, fixture.Session.Handle));
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
                fixture.Combat.TryStrikeEnvironment(fixture.Born.Handle, 1, 0f, 1));
            Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var hit));
            AssertState(row.GetProperty("afterHit"), hit, fixture.Random, context + "/hit");
            Assert.True(hit.Simulation.JustHit);

            var killed = fixture.Combat.TryStrikeEnvironment(fixture.Born.Handle, 100_000, 0f, 1);
            Assert.True(killed == RuntimeTownNpcMeleeDamageResult1458.Killed, context + "/lethal result:" + killed);
            Assert.False(fixture.Npcs.TryGet(fixture.Born.Handle, out _));
            int expectedNext = row.GetProperty("next").GetInt32(), actualNext = fixture.Random.Clone().Next();
            Assert.True(expectedNext == actualNext, $"{context}/death RNG: expected{expectedNext}, actual{actualNext}");
            var items = new WorldItemSnapshot[fixture.Items.Capacity];
            int count = fixture.Items.CopyActive(items);
            var drops = row.GetProperty("drops");
            Assert.Equal(drops.GetArrayLength(), count);
            for (int index = 0; index < count; index++)
            {
                var expected = drops[index]; var actual = items[index];
                Assert.Equal((expected.GetProperty("id").GetInt32(), expected.GetProperty("stack").GetInt32(),
                    expected.GetProperty("prefix").GetInt32()),
                    ((int)actual.ItemNetId, (int)actual.Stack, (int)actual.Prefix));
                Assert.Equal((expected.GetProperty("x").GetSingle(), expected.GetProperty("y").GetSingle(),
                    expected.GetProperty("vx").GetSingle(), expected.GetProperty("vy").GetSingle()),
                    (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
            }
            Assert.Equal(0, fixture.Replication.UnsupportedCommits);
            Assert.Equal(0, fixture.Replication.RejectedFrames);
            Assert.Empty(fixture.Replication.CaptureWorldTransferDespawnFrames());
            Assert.True(late.QueuedFrames > 0);
            var afterDeath = Join(fixture.Replication, 781460);
            Assert.Equal(0, afterDeath.QueuedFrames);
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Combat.TryStrikeEnvironment(fixture.Born.Handle, 100_000));
            Assert.Equal(default, fixture.Tick());
            Assert.Equal(default, fixture.Tick());
            Assert.True(fixture.Npcs.TrySpawnIntent(new NpcAiSpawnIntent(fixture.Born.TypeIdentity, 809, 480, 0, 0, 255)
                { NetIdOverride = fixture.Born.NetIdentity }, out var replacement));
            Assert.Equal(fixture.Born.Handle.Slot, replacement.Handle.Slot);
            Assert.NotEqual(fixture.Born.Handle.Generation, replacement.Handle.Generation);
            var beforeStale = fixture.Random.Clone();
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Combat.TryStrikeEnvironment(fixture.Born.Handle, 100_000));
            Assert.True(fixture.Npcs.TryGet(replacement.Handle, out var retained));
            Assert.Equal(replacement, retained);
            Assert.False(fixture.Combat.Interactions.HasAnyInteraction(replacement.Handle));
            Assert.True(beforeStale.HasSameState(fixture.Random));
            cases++;
        }
        Assert.Equal(240, cases);
        AssertNoMissingFrames(missingFrames);
    }

    [Fact]
    public void New_positive_skeletons_participate_in_the_complete_existing_original_death_matrix()
    {
        int cases = 0;
        foreach (var data in UndeadLoot1458Tests.Rows("death"))
        {
            string json = (string)data[0];
            using var document = JsonDocument.Parse(json);
            int type = document.RootElement.GetProperty("profile").GetProperty("Type").GetInt32();
            if (type is not (201 or 202 or 203 or 635)) continue;
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.True(definition.SupportsRuntimeLifecycle);
            cases++;
        }
        Assert.Equal(3120, cases);
    }

    [Fact]
    public void CheckActive_uses_committed_odd_body_integer_centres_and_actual_player_hitboxes()
    {
        var npcs = new RuntimeNpcStore(1);
        Assert.True(npcs.TrySpawnVanilla(new(3, -27, 0, 0, 0, 0, 255, default,
            NpcSimulationState.Initial with { TimeLeft = 10, HitboxOverride = new(47, 35) }), out var born));
        var wrapper = new VanillaNpcCheckActiveAiStepper(new Retain());
        // NPC.CheckActive (1.4.5.8) gives this odd body's reset rectangle right edge1030.
        // An even/classic body misses the first intersection; a floating-point half-width
        // moves the second edge to1031 after Rectangle integer truncation.
        wrapper.SetCandidates([new(0, 1039, 0, 0, true, false, false, false)]);
        Assert.True(wrapper.TryStepState(in born, out var intersects));
        Assert.Equal(749, intersects.Simulation.TimeLeft);
        wrapper.SetCandidates([new(0, 1040, 0, 0, true, false, false, false)]);
        Assert.True(wrapper.TryStepState(in born, out var touching));
        Assert.Equal(9, touching.Simulation.TimeLeft);
        wrapper.SetCandidates([new VanillaNpcTargetCandidate(0, 1080, 0, 0, true, false, false, false)
            { HitboxWidth = 102, HitboxHeight = 80 }]);
        Assert.True(wrapper.TryStepState(in born, out var widePlayer));
        Assert.Equal(749, widePlayer.Simulation.TimeLeft);
    }

    [Fact]
    public void Original_target_handoff_day_loss_obstacles_platforms_and_water_finish_through_runtime_updates()
    {
        using var stream = typeof(NpcUndeadVariantsRuntime1458Tests).Assembly.GetManifestResourceStream(
            OperatingSystem.IsWindows() ? "UndeadVariantsTransitionsWindows1458" : "UndeadVariantsTransitionsLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var missingFrames = new List<string>();
        int groups = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            using var fixture = new Fixture(row);
            string scene = row.GetProperty("scene").GetString()!;
            string context = $"{row.GetProperty("requested")}/{scene}/seed{row.GetProperty("seed")}";
            AssertState(row.GetProperty("birth"), fixture.Born, fixture.Random, context + "/birth");
            foreach (var phase in row.GetProperty("phases").EnumerateArray())
            {
                int tick = phase.GetProperty("tick").GetInt32();
                if (scene == "handoff" && tick == 3) fixture.SetPlayers(handoff: true);
                if (scene == "handoff" && tick == 8) fixture.SetPlayers(inactive: true);
                Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), fixture.Tick());
                var expected = phase.GetProperty("state");
                if (expected.GetProperty("active").GetBoolean())
                {
                    Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var actual));
                    AssertState(expected, actual, fixture.Random, context + "/tick" + tick);
                }
                else
                {
                    Assert.False(fixture.Npcs.TryGet(fixture.Born.Handle, out _));
                    Assert.Equal(expected.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
                }
                AssertSourceFrames(phase.GetProperty("frames"), fixture.Live, context + "/tick" + tick +
                    $"/sync={fixture.Probe.LastSync}/commit={fixture.Commits.LastKind}", missingFrames);
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
            Assert.Equal(0, fixture.Replication.UnsupportedCommits);
            groups++;
        }
        Assert.Equal(300, groups);
        AssertNoMissingFrames(missingFrames);
    }

    [Fact]
    public void Original_expert_elevated_target_jump_gates_and_complete_landing_cycles_match()
    {
        using var stream = typeof(NpcUndeadVariantsRuntime1458Tests).Assembly.GetManifestResourceStream(
            OperatingSystem.IsWindows() ? "UndeadVariantsElevatedWindows1458" : "UndeadVariantsElevatedLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var missingFrames = new List<string>();
        int groups = 0, updates = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            using var fixture = new Fixture(row);
            string context = $"{row.GetProperty("requested")}/{row.GetProperty("name")}/gap{row.GetProperty("gap")}" +
                $"/dx{row.GetProperty("dx")}/platform{row.GetProperty("platform")}/visible{row.GetProperty("visible")}";
            AssertState(row.GetProperty("birth"), fixture.Born, fixture.Random, context + "/birth");
            foreach (var phase in row.GetProperty("phases").EnumerateArray())
            {
                int tick = phase.GetProperty("tick").GetInt32();
                Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), fixture.Tick());
                Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var actual));
                AssertState(phase.GetProperty("state"), actual, fixture.Random, context + "/tick" + tick);
                AssertSourceFrames(phase.GetProperty("frames"), fixture.Live, context + "/tick" + tick, missingFrames);
                updates++;
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
            groups++;
        }
        Assert.Equal(1440, groups);
        Assert.Equal(19104, updates);
        AssertNoMissingFrames(missingFrames);
    }

    [Fact]
    public void Actual_nonlethal_strike_flows_through_knockback_JustHit_updates_low_priority_sync_and_death()
    {
        using var stream = typeof(NpcUndeadVariantsRuntime1458Tests).Assembly.GetManifestResourceStream(
            OperatingSystem.IsWindows() ? "UndeadVariantsPostHitWindows1458" : "UndeadVariantsPostHitLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var missingFrames = new List<string>();
        int groups = 0, updates = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            using var fixture = new Fixture(row);
            string context = $"{row.GetProperty("requested")}/{row.GetProperty("name")}/post-hit";
            foreach (var phase in row.GetProperty("phases").EnumerateArray())
            {
                Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), fixture.Tick());
                Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var actual));
                AssertState(phase.GetProperty("state"), actual, fixture.Random, context + "/before-hit/tick" + phase.GetProperty("tick"));
                AssertSourceFrames(phase.GetProperty("frames"), fixture.Live, context, missingFrames);
            }
            Assert.True(fixture.Combat.Interactions.TryMark(fixture.Born.Handle, fixture.Session.Handle));
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
                fixture.Combat.TryStrikeEnvironment(fixture.Born.Handle, row.GetProperty("hitDamage").GetInt32(),
                    row.GetProperty("hitKnockback").GetSingle(), row.GetProperty("hitDirection").GetInt32()));
            Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var hit));
            AssertState(row.GetProperty("afterHit"), hit, fixture.Random, context + "/strike");
            Assert.True(hit.Simulation.JustHit);
            AssertSourceFrames(row.GetProperty("hitFrames"), fixture.Live, context + "/strike", missingFrames);
            foreach (var phase in row.GetProperty("postHitPhases").EnumerateArray())
            {
                int tick = phase.GetProperty("tick").GetInt32();
                Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var before));
                AssertState(phase.GetProperty("before"), before, fixture.Random, context + "/before" + tick);
                Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), fixture.Tick());
                Assert.True(fixture.Npcs.TryGet(fixture.Born.Handle, out var actual));
                AssertState(phase.GetProperty("state"), actual, fixture.Random, context + "/tick" + tick);
                Assert.False(actual.Simulation.JustHit);
                Assert.Equal(tick == 1, fixture.Commits.LastLowPriority);
                AssertSourceFrames(phase.GetProperty("frames"), fixture.Live, context + "/tick" + tick, missingFrames);
                updates++;
            }
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                fixture.Combat.TryStrikeEnvironment(fixture.Born.Handle, 100_000, 0f, 1));
            Assert.False(fixture.Npcs.TryGet(fixture.Born.Handle, out _));
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
            var actualDrops = new WorldItemSnapshot[fixture.Items.Capacity];
            int count = fixture.Items.CopyActive(actualDrops);
            var drops = row.GetProperty("drops");
            Assert.Equal(drops.GetArrayLength(), count);
            for (int index = 0; index < count; index++)
            {
                var expected = drops[index]; var actual = actualDrops[index];
                try
                {
                    Assert.Equal((expected.GetProperty("id").GetInt32(), expected.GetProperty("stack").GetInt32(),
                        expected.GetProperty("prefix").GetInt32(), expected.GetProperty("x").GetSingle(),
                        expected.GetProperty("y").GetSingle(), expected.GetProperty("vx").GetSingle(),
                        expected.GetProperty("vy").GetSingle()),
                        ((int)actual.ItemNetId, (int)actual.Stack, (int)actual.Prefix, actual.PositionX,
                            actual.PositionY, actual.VelocityX, actual.VelocityY));
                }
                catch (Xunit.Sdk.XunitException failure)
                { throw new Xunit.Sdk.XunitException(context + "/death/drop" + index + ": " + failure); }
            }
            groups++;
        }
        Assert.Equal(90, groups);
        Assert.Equal(5490, updates);
        AssertNoMissingFrames(missingFrames);
    }

    private sealed class Retain : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX,
                npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
            return true;
        }
    }

    private sealed class ProposalProbe(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        internal bool LastSync;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            bool success = inner.TryStepState(in npc, out next);
            LastSync = success && next.RequiresImmediateSync;
            return success;
        }
    }

    private sealed class CommitProbe(RuntimeNpcReplicationRegistry replication) : INpcStateCommitSink, INpcBirthRetentionSink
    {
        internal NpcStateCommitKind? LastKind;
        internal bool LastLowPriority;
        public void NpcBirthRetained(in NpcSnapshot snapshot) => replication.NpcBirthRetained(in snapshot);
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            LastKind = kind;
            replication.NpcStateCommitted(kind, in snapshot);
        }
        public void NpcVanillaAiStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot, bool lowPriority = false)
        {
            LastKind = kind;
            LastLowPriority = lowPriority;
            replication.NpcVanillaAiStateCommitted(kind, in snapshot, lowPriority);
        }
    }

    private static TerrariaConnectionOutboundQueue AssertLateJoin(Fixture fixture, JsonElement expected)
    {
        var queue = Join(fixture.Replication, 781459);
        Assert.Equal(1, queue.QueuedFrames);
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        Assert.True(inner.TryRead(out var frame));
        var bytes = frame.Bytes.Span;
        // Independent original packet23 layout: length/id, physical slot/generation, four Single
        // components, ushort target, two flag bytes, signed NetID. The source corpus retains zero ai[].
        Assert.Equal(bytes.Length, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal(23, bytes[2]);
        Assert.Equal(fixture.Born.Handle.Slot, bytes[3]);
        Assert.Equal(1, bytes[4]);
        Assert.Equal(expected.GetProperty("x").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(bytes[5..]));
        Assert.Equal(expected.GetProperty("y").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(bytes[9..]));
        Assert.Equal(expected.GetProperty("vx").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(bytes[13..]));
        Assert.Equal(expected.GetProperty("vy").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(bytes[17..]));
        Assert.Equal(expected.GetProperty("target").GetInt32(), BinaryPrimitives.ReadUInt16LittleEndian(bytes[21..]));
        Assert.Equal(8, bytes[24] & 8); // A late join must reset its physical NPC slot.
        Assert.Equal(expected.GetProperty("netId").GetInt32(), BinaryPrimitives.ReadInt16LittleEndian(bytes[25..]));
        return queue;
    }

    private static void AssertSourceFrames(JsonElement frames, TerrariaConnectionOutboundQueue outbound,
        string context, List<string> missing)
    {
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        var actual = new List<string>();
        while (inner.TryRead(out var frame)) actual.Add(Convert.ToHexString(frame.Bytes.Span));
        // Source netUpdate transitions must publish immediately. Additional bounded motion-stream
        // samples are an existing documented runtime slice, not complete vanilla wire-cadence parity.
        int nextMatch = 0;
        foreach (var frame in frames.EnumerateArray())
        {
            int match = actual.IndexOf(frame.GetString()!, nextMatch);
            if (match >= 0) nextMatch = match + 1;
            else missing.Add(context + "/missing source frame:" + frame.GetString() +
                "; actual:" + string.Join(",", actual));
        }
    }

    private static void AssertNoMissingFrames(List<string> missing) =>
        Assert.True(missing.Count == 0, $"Missing {missing.Count} original frames:\n" + string.Join("\n", missing));

    private static TerrariaConnectionOutboundQueue Join(RuntimeNpcReplicationRegistry replication, int sourceId)
    {
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 1024));
        var source = GameCommandSourceId.FromConnection(sourceId);
        Assert.True(replication.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new PlayerSlotId(0), new(1)));
        replication.PlayerSpawned(connection, new PlayerSpawnCommitRequest(connection.Player.Slot, 0, 0, 0, 0, 0, 0, 0));
        return queue;
    }

    private static void AssertState(JsonElement expected, in NpcSnapshot actual, VanillaUnifiedRandom1458 random, string context)
    {
        try { AssertStateCore(expected, actual, random); }
        catch (Xunit.Sdk.XunitException failure) { throw new Xunit.Sdk.XunitException(context + ": " + failure); }
    }

    private static void AssertStateCore(JsonElement expected, in NpcSnapshot actual, VanillaUnifiedRandom1458 random)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(actual.TypeIdentity, actual.NetIdentity, out var definition));
        Assert.True(definition.SupportsRuntimeLifecycle);
        Assert.True(definition.TryResolveHitbox(actual.Simulation, out var body));
        Assert.Equal((expected.GetProperty("type").GetInt32(), expected.GetProperty("netId").GetInt32()),
            (actual.Type, (int)actual.NetId));
        Assert.Equal((expected.GetProperty("width").GetInt32(), expected.GetProperty("height").GetInt32()),
            (body.Width, body.Height));
        Assert.Equal((expected.GetProperty("x").GetSingle(), expected.GetProperty("y").GetSingle(),
            expected.GetProperty("vx").GetSingle(), expected.GetProperty("vy").GetSingle()),
            (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
        Assert.Equal((expected.GetProperty("oldX").GetSingle(), expected.GetProperty("oldY").GetSingle()),
            (actual.Simulation.OldPositionX, actual.Simulation.OldPositionY));
        Assert.Equal(expected.GetProperty("scale").GetSingle(), actual.Simulation.Scale);
        Assert.Equal(expected.GetProperty("life").GetInt32(), actual.Simulation.Life);
        Assert.Equal(expected.GetProperty("lifeMax").GetInt32(), actual.Simulation.LifeMax);
        Assert.Equal(expected.GetProperty("damage").GetInt32(), actual.Simulation.DamageOverride ?? definition.Damage);
        Assert.Equal(expected.GetProperty("defense").GetInt32(), actual.Simulation.DefenseOverride ?? definition.Defense);
        Assert.Equal(expected.GetProperty("knockback").GetSingle(), actual.Simulation.KnockBackResist);
        Assert.Equal(expected.GetProperty("value").GetSingle(), actual.Simulation.MoneyValue);
        Assert.Equal(expected.GetProperty("alpha").GetInt32(), actual.Simulation.Alpha);
        Assert.Equal(expected.GetProperty("target").GetInt32(), actual.Target);
        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("directionY").GetInt32(), actual.Simulation.DirectionY);
        Assert.Equal(expected.GetProperty("sprite").GetInt32(), actual.Simulation.SpriteDirection);
        Assert.Equal(expected.GetProperty("ai").EnumerateArray().Select(item => item.GetSingle()),
            new[] { actual.Ai.Ai0, actual.Ai.Ai1, actual.Ai.Ai2, actual.Ai.Ai3 });
        Assert.Equal(expected.GetProperty("localAi").EnumerateArray().Select(item => item.GetSingle()),
            new[] { actual.Simulation.LocalAi.Ai0, actual.Simulation.LocalAi.Ai1,
                actual.Simulation.LocalAi.Ai2, actual.Simulation.LocalAi.Ai3 });
        Assert.Equal(expected.GetProperty("wet").GetBoolean(), actual.Simulation.Wet);
        Assert.Equal(expected.GetProperty("collideX").GetBoolean(), actual.Simulation.CollideX);
        Assert.Equal(expected.GetProperty("collideY").GetBoolean(), actual.Simulation.CollideY);
        Assert.Equal(expected.GetProperty("noGravity").GetBoolean(), actual.Simulation.NoGravity);
        Assert.Equal(expected.GetProperty("noTileCollide").GetBoolean(), actual.Simulation.NoTileCollide);
        Assert.Equal(expected.GetProperty("dontTakeDamage").GetBoolean(), actual.Simulation.DontTakeDamage);
        if (expected.TryGetProperty("justHit", out var justHit)) Assert.Equal(justHit.GetBoolean(), actual.Simulation.JustHit);
        Assert.Equal(expected.GetProperty("next").GetInt32(), random.Clone().Next());
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly RuntimeNpcReplicationRegistry Replication = new();
        internal readonly TerrariaConnectionOutboundQueue Live;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly PlayerJoinSession Session;
        internal readonly NpcSnapshot Born;
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly VanillaNpcTargetingAiStepper Targeting;
        internal readonly VanillaNpcCheckActiveAiStepper CheckActive;
        internal readonly ProposalProbe Probe;
        internal readonly CommitProbe Commits;
        private readonly RuntimeNpcAiStateExecutor executor;
        private readonly RawPlayers raw = new();

        internal Fixture(JsonElement row)
        {
            string? scene = row.TryGetProperty("scene", out var sceneValue) ? sceneValue.GetString() : null;
            double surface = row.TryGetProperty("worldSurface", out var surfaceValue) ? surfaceValue.GetDouble() : 40d;
            bool isThereSurface = row.TryGetProperty("isThereAWorldSurface", out var surfacePresent) && surfacePresent.GetBoolean();
            Random = new(row.TryGetProperty("seed", out var seed) ? seed.GetInt32() : 1458);
            var tiles = new WorldTileStore(new WorldDimensions(100, 80));
            for (int x = 0; x < 100; x++)
            {
                tiles.Set(x, 30, new WorldTile { Type = (ushort)(scene == "platform" ? 19 : 1), Flags = WorldTileFlags.Active });
                if (scene == "platform") tiles.Set(x, 40, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            }
            if (scene == "obstacle")
                for (int y = 28; y <= 29; y++)
                    tiles.Set(52, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            if (scene == "water")
                for (int x = 48; x <= 65; x++)
                    for (int y = 25; y <= 29; y++)
                        tiles.Set(x, y, new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Water });
            if (scene == "expert-jump")
            {
                var birth = row.GetProperty("birth");
                if (row.GetProperty("platform").GetBoolean())
                {
                    int tileX = (int)((birth.GetProperty("x").GetSingle() + birth.GetProperty("width").GetInt32() * .5f) / 16f);
                    tiles.Set(tileX, 25, new WorldTile { Type = 19, Flags = WorldTileFlags.Active });
                }
                if (!row.GetProperty("visible").GetBoolean())
                    for (int x = 48; x <= 53; x++)
                        tiles.Set(x, 25, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            }
            var authority = new PlayerAuthority(null, null);
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            Session = new(lease!); Session.ObserveWorldRequest(); Session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(781458), Session.Handle);
            authority.TryApply(new PlayerHealthRuntimeCommand(connection, new(Session.Slot, 400, 400)));
            authority.TryApply(new PlayerSpawnRuntimeCommand(connection, Session, new(Session.Slot, 0, 0, 0, 0, 0, 0, 0)));
            var lookup = new FixedPlayer(Session.Handle);
            int mode = row.TryGetProperty("mode", out var modeValue) ? modeValue.GetInt32() : 0;
            bool good = row.TryGetProperty("good", out var goodValue) && goodValue.GetBoolean();
            float difficulty = mode + 1 + (good ? 1 : 0);
            Commits = new(Replication);
            Npcs = new(2, commitSink: Commits);
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            Npcs.SetVanillaSpawnContextSource(() => new(difficulty, 1, good));
            Npcs.SetVanillaSpawnWetCollisionSource((x, y, width, height) =>
                VanillaWorldCollision.GetLiquidContacts(tiles, x, y, width, height).Wet);
            Live = Join(Replication, 781457);
            int type = row.GetProperty("birth").GetProperty("type").GetInt32();
            int requested = row.GetProperty("requested").GetInt32();
            var pending = new NpcStateUpdate(type, checked((short)requested), 0, 0, 0, 0, 255, default,
                NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
            Assert.True(Npcs.TrySpawnVanillaPendingAtBottomCenter(in pending, 809, 480, out Born));
            Assert.Equal(0, Live.QueuedFrames);
            Targeting = new(new VanillaDemonEyeAiStepper(), random: new SystemVanillaNpcRandom(Random));
            var motion = new VanillaNpcWorldMotionAiStepper(Targeting, tiles, surface);
            Targeting.SetWorldConditions(dayTime: scene == "far-day", slimeRainActive: false,
                goodWorld: good, expertMode: difficulty >= 2f, masterMode: difficulty >= 3f);
            Targeting.SetRawPlayerSlots(raw);
            CheckActive = new(motion);
            raw.Zero = new(0, true, false, false, scene == "far-day" ? 3000 : 900,
                scene == "platform" ? 600 : 438, 20, 42, 0, false, 0);
            if (scene == "expert-jump") raw.Zero = raw.Zero with
                { PositionX = row.GetProperty("playerX").GetSingle(), PositionY = row.GetProperty("playerY").GetSingle() };
            SetPlayers();
            Probe = new(CheckActive);
            executor = new(Npcs);
            Combat = new(Npcs, Items, lookup, authority, () => 0, Replication, new(Items), null,
                new RuntimeWorldClock(1000, false, 0, 0, 1, getGoodWorld: good), new(), difficulty >= 2f, difficulty >= 3f,
                // Captured from Main.isThereAWorldSurface: the transition corpus uses surface40/false.
                isThereAWorldSurface: isThereSurface,
                lootRandom: Random, seasonalItemContext: () => new(false, false, false),
                onlyShimmerOceanWorlds: false,
                mechanicalLootBaseline: new(0, false, false, false, false),
                lootRemixWorld: false, npcSpecificLowTiles: () => false, npcSpecificGoodWorld: good,
                globalLootWorld: new(100, 80, difficulty, false, false, false, false, 35d, surface, false, false));
        }

        internal NpcAiStateTickSummary Tick()
        {
            Npcs.UpdateProtectedSpawnSlots();
            Replication.AdvanceAuthoritativeTick();
            var result = executor.Tick(Probe);
            Npcs.DespawnExpired();
            return result;
        }

        internal void SetPlayers(bool handoff = false, bool inactive = false)
        {
            if (handoff)
            {
                raw.Zero = raw.Zero with { Dead = true };
                raw.One = new(1, true, false, false, 700, 438, 20, 42, 0, false, 0);
            }
            if (inactive)
            {
                raw.Zero = raw.Zero with { Active = false };
                raw.One = raw.One with { Active = false };
            }
            Targeting.SetCandidates([raw.Zero.Candidate, raw.One.Candidate]);
            CheckActive.SetCandidates([raw.Zero.Candidate, raw.One.Candidate]);
            Replication.PlayerMoved(new ConnectionHandle(GameCommandSourceId.FromConnection(781457), Session.Handle),
                default(PlayerMovementCommitRequest) with
                { PlayerSlot = Session.Slot, PositionX = raw.Zero.PositionX, PositionY = raw.Zero.PositionY });
        }

        public void Dispose() => Session.Dispose();
    }

    private sealed class RawPlayers : INpcRawPlayerSlotLookup1458
    {
        internal VanillaNpcRawPlayer1458 Zero = VanillaNpcRawPlayer1458.Constructor(0);
        internal VanillaNpcRawPlayer1458 One = VanillaNpcRawPlayer1458.Constructor(1);
        public bool TryCapture(byte slot, out NpcRawPlayerSlotSnapshot1458 snapshot)
        {
            var facts = slot switch { 0 => Zero, 1 => One, _ => VanillaNpcRawPlayer1458.Constructor(slot) };
            snapshot = new(facts, 1, 1, null, ZoneGraveyard: false);
            return true;
        }
        public bool IsCurrent(in NpcRawPlayerSlotSnapshot1458 snapshot) =>
            TryCapture(snapshot.Facts.Slot, out var current) && current == snapshot;
    }

    private sealed class FixedPlayer(PlayerHandle handle) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with
            {
                Player = handle, Revision = new(1), PositionX = 900f, PositionY = 438f,
                HasHealth = true, Life = 400, MaxLife = 400, DerivedLifeMax = 400,
                HasMana = true, Mana = 200, MaxMana = 200, Zones = default(PlayerZoneSnapshot1458)
            };
            return slot == handle.Slot;
        }
    }
}
