using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class MartianChildPhase1458Tests
{
    public static IEnumerable<object[]> BirthCases() => Rows().Where(x => x.GetProperty("scene").GetString()!.StartsWith("rider-create", StringComparison.Ordinal)).Select(x => new object[] { x });
    public static IEnumerable<object[]> FireCases() => Rows().Where(x => x.GetProperty("scene").GetString() == "rider-fire-parent").Select(x => new object[] { x });
    public static IEnumerable<object[]> OrphanCases() => Rows().Where(x => x.GetProperty("scene").GetString() == "self-orphan").Select(x => new object[] { x });

    [Theory, MemberData(nameof(BirthCases))]
    public void Real_App_mount_observer_sees_adopted_status_parent_child_and_entropy(JsonElement row)
    {
        using var f = new RangedNpcPhaseFixture1458(row);
        var actor = Setup(f, row);
        var expected = row.GetProperty("frames")[0].GetProperty("after");
        int births = 0;
        VanillaUnifiedRandom1458? callbackRandom = null;
        f.OnNpcBirth = birth =>
        {
            if (birth.TypeIdentity != VanillaNpcIds.Scutlix) return;
            births++;
            var current = f.Current(actor.Handle);
            Assert.True(current.Revision.Value >= actor.Revision.Value + 2);
            AssertState(expected.GetProperty("npc"), in current);
            var sourceChild = expected.GetProperty("activeNpcPeers").EnumerateArray().Single(x => x.GetProperty("slot").GetInt32() == birth.Handle.Slot).GetProperty("state");
            AssertState(sourceChild, in birth);
            Assert.Equal((float)birth.Handle.Slot, current.Ai.Ai0);
            Assert.True(f.Status.TryGetWireDuration(actor.Handle, out int duration));
            Assert.Equal(row.GetProperty("scene").GetString()!.Contains("water", StringComparison.Ordinal) ? -1 : 5, duration);
            Assert.True(f.Status.TryGetStinky(actor.Handle, out bool stinky));
            Assert.True(stinky); // Wet cure removes the source slot, not this update's already-set flag.
            Assert.Equal(0, f.Projectiles.ActiveCount); // A fresh mount still targets the constructor slot255.
            RangedNpcPhaseFixture1458.AssertRandom(expected.GetProperty("rng"), f.Random);
            Assert.False(f.Targeting.TryStepState(in current, out _));
            callbackRandom = f.Random.Clone(); callbackRandom.Next(); f.Random.Next();
        };
        var completed = f.Step(actor.Handle);
        Assert.Equal(1, births);
        AssertState(expected.GetProperty("npc"), in completed);
        Assert.True(f.Random.HasSameState(Assert.IsType<VanillaUnifiedRandom1458>(callbackRandom)));
        Assert.False(f.RandomInputImported);
    }

    [Theory, MemberData(nameof(BirthCases))]
    public void Actual_NpcAuthority_phase_reaches_the_same_mount_observer_before_later_slot_updates(JsonElement row)
    {
        using var f = new RangedNpcPhaseFixture1458(row);
        var actor = Setup(f, row);
        var expected = row.GetProperty("frames")[0].GetProperty("after");
        int births = 0;
        f.OnNpcBirth = born =>
        {
            if (born.TypeIdentity != VanillaNpcIds.Scutlix) return;
            births++;
            var current = f.Current(actor.Handle);
            AssertState(expected.GetProperty("npc"), in current);
            RangedNpcPhaseFixture1458.AssertRandom(expected.GetProperty("rng"), f.Random);
            Assert.True(f.Status.TryGetWireDuration(actor.Handle, out int duration));
            Assert.Equal(row.GetProperty("scene").GetString()!.Contains("water", StringComparison.Ordinal) ? -1 : 5, duration);
        };
        f.TickWholeNpcPhase();
        Assert.Equal(1, births);
        // The authority subsequently executes slot1; its additional AI/RNG is outside
        // the original individual390 observation and is deliberately not called parity here.
    }

    [Theory, MemberData(nameof(FireCases))]
    public void Real_existing_mount_prepass_and_390_phase_publish_the_independent_positive_438(JsonElement row)
    {
        using var f = new RangedNpcPhaseFixture1458(row);
        var component = row.GetProperty("component");
        var beforeParent = component.GetProperty("before");
        var input = row.GetProperty("input");
        var born = f.SpawnObserved(beforeParent.GetProperty("npc"), 1023, row.GetProperty("floor").GetInt32() * 16);
        f.AssertBirth(row.GetProperty("birth"));
        var actor = f.ImportObserved(born.Handle, beforeParent.GetProperty("npc"));
        var sourceParent = beforeParent.GetProperty("activeNpcPeers").EnumerateArray().Single(x => x.GetProperty("slot").GetInt32() == 1).GetProperty("state");
        var parent = f.SpawnObserved(sourceParent, CenterX(sourceParent), BottomY(sourceParent), 1, 0);
        parent = f.ImportObserved(parent.Handle, sourceParent);
        RangedNpcPhaseFixture1458.AssertRandom(beforeParent.GetProperty("rng"), f.Random);
        var movedParent = f.Step(parent.Handle);
        var expectedParent = component.GetProperty("after").GetProperty("activeNpcPeers").EnumerateArray().Single(x => x.GetProperty("slot").GetInt32() == 1).GetProperty("state");
        AssertState(expectedParent, in movedParent);
        RangedNpcPhaseFixture1458.AssertRandom(input.GetProperty("rng"), f.Random);
        var completed = f.Step(actor.Handle);
        var after = row.GetProperty("frames")[0].GetProperty("after");
        AssertState(after.GetProperty("npc"), in completed);
        RangedNpcPhaseFixture1458.AssertRandom(after.GetProperty("rng"), f.Random);
        Span<ProjectileSnapshot> shots = stackalloc ProjectileSnapshot[1];
        Assert.Equal(1, f.Projectiles.CopyActive(shots));
        var expected = Assert.Single(after.GetProperty("projectiles").EnumerateArray());
        Assert.Equal(438, shots[0].Type.Value);
        Assert.Equal(expected.GetProperty("damage").GetInt32(), (int)shots[0].Damage);
        Assert.Equal(new[] { F(expected.GetProperty("position")[0]), F(expected.GetProperty("position")[1]), F(expected.GetProperty("velocity")[0]), F(expected.GetProperty("velocity")[1]) },
            new[] { shots[0].PositionX, shots[0].PositionY, shots[0].VelocityX, shots[0].VelocityY });
        Assert.Equal(0, (int)shots[0].OriginalDamage);
        Assert.Equal((byte)255, shots[0].Spawner);
        Assert.True(f.Projectiles.TryGetServerNpcSource(shots[0].Handle, out var owner));
        Assert.Equal(actor.Handle, owner);
        Assert.Equal(after.GetProperty("notifications").EnumerateArray().Select(x => x.GetString()).ToArray(),
            f.Drain().Select(Convert.ToHexStringLower).ToArray());
        Assert.False(f.RandomInputImported);
    }

    [Theory, MemberData(nameof(OrphanCases))]
    public void Self_parent_orphan_retires_after_motion_with_one_exact_inactive_packet_and_no_countdown(JsonElement row)
    {
        using var f = new RangedNpcPhaseFixture1458(row);
        var actor = Setup(f, row);
        var completed = f.Step(actor.Handle);
        var expected = row.GetProperty("frames")[0].GetProperty("after");
        AssertState(expected.GetProperty("npc"), in completed);
        Assert.Equal(750, completed.Simulation.TimeLeft);
        Assert.False(f.Npcs.TryGet(actor.Handle, out _));
        Assert.Equal(expected.GetProperty("npc").GetProperty("netSpam").GetInt32(),
            f.Registry.CaptureNpcPhaseSpam(actor.Handle));
        RangedNpcPhaseFixture1458.AssertRandom(expected.GetProperty("rng"), f.Random);
        Assert.Equal(expected.GetProperty("notifications").EnumerateArray().Select(x => x.GetString()).ToArray(),
            f.Drain().Select(Convert.ToHexStringLower).ToArray());
    }

    [Theory, MemberData(nameof(OrphanCases))]
    public void Actual_NpcAuthority_orphan_retirement_reaches_the_same_single_packet_before_later_slots(JsonElement row)
    {
        using var f = new RangedNpcPhaseFixture1458(row);
        var actor = Setup(f, row);
        var expected = row.GetProperty("frames")[0].GetProperty("after");
        int retirements = 0;
        f.OnNpcCommit = (kind, snapshot) =>
        {
            if (kind != NpcStateCommitKind.Despawn || snapshot.Handle != actor.Handle) return;
            retirements++;
            AssertState(expected.GetProperty("npc"), in snapshot);
            Assert.False(f.Npcs.TryGet(actor.Handle, out _));
            Assert.Equal(expected.GetProperty("npc").GetProperty("netSpam").GetInt32(),
                f.Registry.CaptureNpcPhaseSpam(actor.Handle));
            RangedNpcPhaseFixture1458.AssertRandom(expected.GetProperty("rng"), f.Random);
            Assert.Equal(expected.GetProperty("notifications").EnumerateArray().Select(x => x.GetString()).ToArray(),
                f.Drain().Select(Convert.ToHexStringLower).ToArray());
        };
        f.TickWholeNpcPhase();
        Assert.Equal(1, retirements);
        // Other source slots were held constant in this individual actor oracle.
        // This observation stops at its retirement, before the authority runs slot1.
    }

    [Fact]
    public void Mount_observer_replacement_keeps_only_the_new_generation_stinky_frame()
    {
        var row = Rows().First(x => x.GetProperty("scene").GetString() == "rider-create-water-stinky");
        using var f = new RangedNpcPhaseFixture1458(row);
        var actor = Setup(f, row);
        NpcSnapshot replacement = default;
        f.OnNpcBirth = born =>
        {
            if (born.TypeIdentity != VanillaNpcIds.Scutlix) return;
            Assert.True(f.Status.TryGetWireDuration(actor.Handle, out int duration));
            Assert.Equal(-1, duration);
            Assert.True(f.Npcs.TryDespawn(actor.Handle));
            var next = new NpcStateUpdate(390, 390, 400f, 400f, 0f, 0f, 255, default, NpcSimulationState.Initial);
            Assert.True(f.Npcs.TrySpawn(actor.Handle.Slot, in next, out replacement));
            f.World.Apply(new ClientNpcBuffRuntimeCommand(f.Connection, new(replacement.Handle.Slot, 120, 90)));
        };
        f.Step(actor.Handle);
        Assert.NotEqual(actor.Handle.Generation, replacement.Handle.Generation);
        Assert.Equal(replacement, f.Current(replacement.Handle));
        Assert.True(f.Status.TryGetWireDuration(replacement.Handle, out int newest));
        Assert.Equal(90, newest);
        var frame = Assert.Single(f.Drain(), x => x[2] == 54);
        Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(replacement.Handle.Slot, 90, out var expected));
        Assert.Equal(expected, frame);
    }

    private static NpcSnapshot Setup(RangedNpcPhaseFixture1458 f, JsonElement row)
    {
        var input = row.GetProperty("input");
        var npc = input.GetProperty("npc");
        var born = f.SpawnObserved(npc, 1023, row.GetProperty("floor").GetInt32() * 16);
        f.AssertBirth(row.GetProperty("birth"));
        var actor = f.ImportObserved(born.Handle, npc);
        foreach (var entry in input.GetProperty("activeNpcPeers").EnumerateArray())
        {
            byte slot = entry.GetProperty("slot").GetByte();
            if (slot == actor.Handle.Slot) continue;
            var source = entry.GetProperty("state");
            var peer = f.SpawnObserved(source, CenterX(source), BottomY(source), slot, (ushort)source.GetProperty("target").GetInt32());
            f.ImportObserved(peer.Handle, source);
        }
        f.ImportStatus(actor.Handle, npc);
        RangedNpcPhaseFixture1458.AssertRandom(input.GetProperty("rng"), f.Random);
        AssertState(npc, in actor);
        return actor;
    }

    private static void AssertState(JsonElement expected, in NpcSnapshot actor)
    {
        MartianChildAi1458Tests.AssertActor(expected, in actor);
        Assert.Equal(new[] { F(expected.GetProperty("oldPosition")[0]), F(expected.GetProperty("oldPosition")[1]), F(expected.GetProperty("oldVelocity")[0]), F(expected.GetProperty("oldVelocity")[1]) },
            new[] { actor.Simulation.OldPositionX, actor.Simulation.OldPositionY, actor.Simulation.OldVelocityX, actor.Simulation.OldVelocityY });
    }
    private static IEnumerable<JsonElement> Rows()
    {
        using var raw = typeof(MartianChildPhase1458Tests).Assembly.GetManifestResourceStream("MartianChildPhaseLinux1458")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        return data.RootElement.GetProperty("cases").EnumerateArray().Select(x => x.Clone()).ToArray();
    }
    private static int CenterX(JsonElement npc) => (int)(F(npc.GetProperty("position")[0]) + npc.GetProperty("width").GetInt32() * .5f);
    private static int BottomY(JsonElement npc) => (int)(F(npc.GetProperty("position")[1]) + npc.GetProperty("height").GetInt32());
    private static float F(JsonElement x) => BitConverter.UInt32BitsToSingle(uint.Parse(x.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
}
