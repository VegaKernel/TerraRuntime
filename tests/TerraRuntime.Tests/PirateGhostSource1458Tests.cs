using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class PirateGhostSource1458Tests
{
    [Fact]
    public void Actual_source_defaults_match_six_pirates_and_ghost_in_all_owned_difficulties()
    {
        using var ordinary = Read("PirateNpcDefaults1458");
        using var ghost = Read("PirateGhost1458");
        foreach (var row in ordinary.RootElement.GetProperty("rows").EnumerateArray())
            AssertDefaults(row, row.GetProperty("type").GetInt32(), false);
        foreach (var row in ghost.RootElement.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("phase").GetString() == "SetDefaults") AssertDefaults(row, 662, false);
        using var windows = Read("PirateNpcDefaultsWindows1458", windows: true);
        Assert.Equal(32, windows.RootElement.GetProperty("runtime").GetProperty("processBits").GetInt32());
        foreach (var row in windows.RootElement.GetProperty("rows").EnumerateArray())
            AssertDefaults(row, row.GetProperty("type").GetInt32(), true);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.PirateGhost, out var definition));
        Assert.True(definition.DefinitionOnly);
        Assert.Equal(0, definition.BaseWidth);
        Assert.Equal(0, definition.BaseHeight);
        Assert.Equal(VanillaNpcBehaviorFamily.None, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.None, definition.PhysicsFamily);
        Assert.False(VanillaNpcSpawnDefaults.TryResolve(in definition, new(1.5f, 1, false), out _));
    }

    [Fact]
    public void Actual_AI122_motion_facing_peer_separation_and_fade_intent_match_source_without_mutation()
    {
        using var document = Read("PirateGhost1458");
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("phase").GetString() != "AI_122_PirateGhost") continue;
            string profile = row.GetProperty("profile").GetString()!;
            var actor = Snapshot(row.GetProperty("before"));
            var stepper = new VanillaNpcTargetingAiStepper(new Unavailable());
            bool living = !profile.StartsWith("no-living", StringComparison.Ordinal) && profile != "dead-player";
            var raw = new VanillaNpcRawPlayer1458(0, living || profile == "dead-player", profile == "dead-player",
                false, 1000f, 440f, 20, 42, 0, false, 0);
            stepper.SetRawPlayerSlots(new Raw(raw));
            stepper.SetCandidates([raw.Candidate]);
            if (profile.StartsWith("peer", StringComparison.Ordinal))
            {
                var peer = actor with { Handle = new(1, new(1)), PositionX = profile == "peer-near" ? 825f :
                    profile == "peer-far" ? 900f : 800f, VelocityX = 0f, VelocityY = 0f };
                if (profile == "peer-wrong-type") peer = peer with { Type = 82, NetId = 82 };
                stepper.SetNpcPeers([peer, actor]);
            }
            var retained = actor;
            bool admitted = stepper.TryPlanPirateGhost(in actor, out var next, out bool fatal);
            Assert.Equal(retained, actor);
            if (profile == "peer-coincident")
            {
                Assert.Equal("NaN", row.GetProperty("after").GetProperty("state").GetProperty("vx").GetString());
                Assert.False(admitted);
                continue;
            }
            Assert.True(admitted, profile);
            AssertPlan(row.GetProperty("after"), in next);
            Assert.Equal(profile.StartsWith("no-living", StringComparison.Ordinal), fatal);
            Assert.Equal(actor.Simulation.Life, next.Simulation.Life); // Strike9999 is a separate retained owner.
            Assert.Equal(actor.PositionX, next.PositionX);
            Assert.Equal(actor.PositionY, next.PositionY);
            Assert.Equal(actor.Ai, next.Ai);
        }
    }

    [Fact]
    public void Source_private_continuous_fade_reaches_one_fatal_intent_and_unknown_raw_is_refused()
    {
        using var document = Read("PirateGhost1458");
        var row = document.RootElement.GetProperty("rows").EnumerateArray().Single(r =>
            r.GetProperty("phase").GetString() == "private-AI122-continuous");
        var stepper = new VanillaNpcTargetingAiStepper(new Unavailable());
        var rawTarget = row.GetProperty("rawTarget");
        stepper.SetRawPlayerSlots(new Raw(new(0, rawTarget.GetProperty("active").GetBoolean(),
            rawTarget.GetProperty("dead").GetBoolean(), false, rawTarget.GetProperty("x").GetSingle(),
            rawTarget.GetProperty("y").GetSingle(), 20, 42, 0, false, 0)));
        int fatalCount = 0;
        foreach (var tick in row.GetProperty("steps").EnumerateArray())
        {
            var before = Snapshot(tick.GetProperty("before"));
            Assert.True(stepper.TryPlanPirateGhost(in before, out var next, out bool fatal));
            AssertPlan(tick.GetProperty("after"), in next);
            if (fatal) fatalCount++;
        }
        Assert.Equal(1, fatalCount);
        var unknown = new VanillaNpcTargetingAiStepper(new Unavailable());
        var initial = Snapshot(row.GetProperty("initial"));
        Assert.False(unknown.TryPlanPirateGhost(in initial, out _, out _));
        var wet = initial with { Simulation = initial.Simulation with { Wet = true } };
        Assert.False(stepper.TryPlanPirateGhost(in wet, out _, out _));
    }

    private static void AssertDefaults(JsonElement row, int type, bool windowsArithmetic)
    {
        var state = row.GetProperty("after").GetProperty("state");
        bool good = row.GetProperty("good").GetBoolean();
        var context = new VanillaNpcSpawnContext(row.GetProperty("mode").GetInt32() + 1f + (good ? 1f : 0f), 1, good);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out var definition));
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windowsArithmetic, out var defaults));
        Assert.Equal(state.GetProperty("width").GetInt32(), defaults.Hitbox.Width);
        Assert.Equal(state.GetProperty("height").GetInt32(), defaults.Hitbox.Height);
        Assert.Equal(state.GetProperty("lifeMax").GetInt32(), defaults.LifeMax);
        Assert.Equal(state.GetProperty("damage").GetInt32(), defaults.Damage);
        Assert.Equal(state.GetProperty("defense").GetInt32(), defaults.Defense);
        Assert.Equal(state.GetProperty("knockBackResist").GetSingle(), defaults.KnockBackResist);
        Assert.Equal(state.GetProperty("scale").GetSingle(), defaults.Scale);
        if (windowsArithmetic) Assert.Equal(state.GetProperty("knockBackBits").GetInt32(),
            BitConverter.SingleToInt32Bits(defaults.KnockBackResist!.Value));
    }

    private static void AssertPlan(JsonElement after, in NpcStateUpdate next)
    {
        var state = after.GetProperty("state");
        Assert.Equal(state.GetProperty("vx").GetSingle(), next.VelocityX);
        Assert.Equal(state.GetProperty("vy").GetSingle(), next.VelocityY);
        Assert.Equal(state.GetProperty("target").GetUInt16(), next.Target);
        Assert.Equal(state.GetProperty("direction").GetInt32(), next.Simulation.DirectionX);
        Assert.Equal(after.GetProperty("alpha").GetInt32(), next.Simulation.Alpha);
        Assert.Equal(after.GetProperty("directionY").GetInt32(), next.Simulation.DirectionY);
        Assert.Equal(Ai(state.GetProperty("localAI")), next.Simulation.LocalAi);
    }

    private static NpcSnapshot Snapshot(JsonElement source)
    {
        var state = source.GetProperty("state");
        return new(new(0, new(1)), new(1), 662, 662,
            state.GetProperty("x").GetSingle(), state.GetProperty("y").GetSingle(),
            state.GetProperty("vx").GetSingle(), state.GetProperty("vy").GetSingle(),
            state.GetProperty("target").GetUInt16(), Ai(state.GetProperty("ai")),
            NpcSimulationState.Initial with { Life = state.GetProperty("life").GetInt32(),
                LifeMax = state.GetProperty("lifeMax").GetInt32(),
                NoGravity = true, NoTileCollide = true, Alpha = source.GetProperty("alpha").GetInt32(),
                LocalAi = Ai(state.GetProperty("localAI")), DirectionX = state.GetProperty("direction").GetInt32(),
                DirectionY = source.GetProperty("directionY").GetInt32() });
    }

    private static NpcAiState Ai(JsonElement array) => new(array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle(), array[3].GetSingle());

    private static JsonDocument Read(string resource, bool windows = false)
    {
        using var stream = typeof(PirateGhostSource1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var document = JsonDocument.Parse(gzip);
        Assert.Equal(windows ? "D87E3FAF08637F6BE8882C63E7F11FB7E792B0230006309618473ECE0F863E1E" :
            "4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",
            document.RootElement.GetProperty("sourceSha256").GetString()!.ToUpperInvariant());
        return document;
    }

    private sealed class Raw(VanillaNpcRawPlayer1458 facts) : INpcRawPlayerSlotLookup1458
    {
        public bool TryCapture(byte slot, out NpcRawPlayerSlotSnapshot1458 snapshot)
        {
            snapshot = new(facts, 1, 1, null);
            return slot == facts.Slot;
        }
        public bool IsCurrent(in NpcRawPlayerSlotSnapshot1458 snapshot) => snapshot.Facts == facts;
    }

    private sealed class Unavailable : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
