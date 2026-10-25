using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ProgressionPacket28Authority1458Tests
{
    [Fact]
    public void Forged_fallback_hits_cannot_damage_or_credit_progression_roots_and_linked_parts()
    {
        // Source NPC.DoDeathEvents/checkDead identifies these roots and linked encounter parts.
        // In particular, Eater/Destroyer segments, WoF eyes, Prime arms and Moon Lord parts
        // are not covered by testing only the root's boss flag.
        int[] types = [4, 13, 14, 15, 35, 36, 50, 113, 114, 125, 126, 127, 128, 129, 130, 131,
            134, 135, 136, 222, 245, 246, 247, 248, 249, 262, 264, 266, 267, 370,
            396, 397, 398, 400, 439, 440, 636, 657, 668];
        foreach (int type in types)
        {
            var npcs = new RuntimeNpcStore(capacity: 1);
            var items = new RuntimeWorldItemStore();
            var progression = new TerraRuntime.World.RuntimeWorldProgressionMutations();
            var random = new VanillaUnifiedRandom1458(1458);
            var players = new PlayerAuthority(null, null);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(10428), session.Handle);
            Assert.True(players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                new(session.Slot, 100, 100, 0, 0, 0, 0, 0))));
            Assert.True(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
                new(session.Slot, 0, 1, 0, (short)VanillaItemIds.WoodenBow.Value, 0))));
            var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new RuntimePlayerSnapshotLookup(players, null),
                players, static () => 0, null, new(items), null, null, progression, false, false, lootRandom: random);
            var input = new NpcStateUpdate(type, (short)type, 100, 100, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 100, LifeMax = 100, DefenseOverride = 0, DontTakeDamage = false });
            Assert.True(npcs.TrySpawn(0, in input, out var npc), $"Missing admitted source type {type}.");
            var beforeRandom = random.Clone();
            foreach (short damage in new short[] { 1, short.MaxValue })
            {
                var wire = new TerrariaNpcDamageState(0,
                    RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation), damage, 0, 2, 0);
                Assert.Equal(RuntimeNpcNetworkDamageResult.Rejected, pipeline.TryApply(connection, in wire));
                Assert.True(npcs.TryGet(npc.Handle, out var unchanged));
                Assert.Equal(npc, unchanged);
                Assert.False(pipeline.Interactions.HasAnyInteraction(npc.Handle));
                Assert.Equal(0, items.ActiveCount);
                Assert.False(progression.CaptureSnapshot().HasAny);
                Assert.True(beforeRandom.HasSameState(random));
            }
        }
    }

    [Fact]
    public void Available_authoritative_melee_math_does_not_turn_a_raw_hit_into_an_owned_swing()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HumanDefensiveCritBuffPhase1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var phase = JsonDocument.Parse(gzip);
        var row = phase.RootElement.GetProperty("linux").EnumerateArray().First(value =>
            value.GetProperty("spec").GetProperty("branch").GetString() == "living" &&
            value.GetProperty("spec").GetProperty("buffs").GetArrayLength() == 0);
        using var fixture = new HumanDefensiveCritBuffPhase1458Tests.Fixture(row, false);
        fixture.Equipment(0, 24, 1); // Wooden Sword, as in the independent owned-phase integration corpus.
        fixture.State.Tick();
        var npcs = new RuntimeNpcStore();
        var items = new RuntimeWorldItemStore();
        var input = new NpcStateUpdate(50, 50, 1612, 1606, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = 1000, LifeMax = 1000 });
        Assert.True(npcs.TrySpawn(0, in input, out var npc));
        Assert.Equal(CombatIntegrityReason.None,
            new AuthoritativeCombatCalculator(fixture.Players, new Random(1458))
                .TryCalculate(fixture.Connection, in npc, 1, out _));
        var progression = new TerraRuntime.World.RuntimeWorldProgressionMutations();
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items,
            new RuntimePlayerSnapshotLookup(fixture.Players, null), fixture.Players,
            static () => 100, null, new(items), null, null, progression, false, false);
        var wire = new TerrariaNpcDamageState(npc.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation), 1, 0, 2, 0);
        Assert.Equal(RuntimeNpcNetworkDamageResult.Rejected, pipeline.TryApply(fixture.Connection, in wire));
        Assert.True(npcs.TryGet(npc.Handle, out var unchanged));
        Assert.Equal(npc, unchanged);
        Assert.False(pipeline.Interactions.HasAnyInteraction(npc.Handle));
        Assert.False(progression.CaptureSnapshot().HasAny);
        Assert.Equal(0, items.ActiveCount);
    }
}
