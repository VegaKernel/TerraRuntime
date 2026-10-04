using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class SlimeContainedInitialization1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(SlimeContainedInitialization1458Tests).Assembly.GetManifestResourceStream("SlimeContainedInitialization1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Complete_original_AI001_selection_state_children_and_stream_match(JsonElement row)
    {
        int I(string key) => row.GetProperty(key).GetInt32();
        float F(string key) => row.GetProperty(key).GetSingle();
        int flags = I("flags"), seed = I("seed"), type = I("type"), net = I("net");
        bool Good() => (flags & 1) != 0;
        var facts = new VanillaSlimeContainedFacts1458(140d, 200d, Good(), (flags & 2) != 0,
            (flags & 4) != 0, (flags & 8) != 0, (flags & 16) != 0, (flags & 32) != 0,
            (flags & 64) != 0, (flags & 128) != 0, (flags & 256) != 0,
            (flags & 64) != 0, true, (flags & 64) != 0, false,
            seed % 2 == 0 ? (int)VanillaMoonPhase.Full : (int)VanillaMoonPhase.Empty);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), new NpcNetId(checked((short)net)), out var definition));
        var random = new SystemVanillaNpcRandom(seed);
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(Good() ? 2f : 1f, 1, Good()));
        int damage = I("initialDamage"), defense = I("initialDefense");
        var input = new NpcStateUpdate(type, checked((short)net), 1000.75f, I("depth") * 16f + .25f,
            .5f, 0f, 0, new(-100f, 0f, 0f, 0f), NpcSimulationState.Initial with
            {
                DirectionX = 1, DirectionY = 1, Life = I("initialLife"), LifeMax = I("initialMax"),
                BaseLifeMax = I("initialMax"), BaseDamage = damage, BaseDefense = defense,
                DamageOverride = damage, DefenseOverride = defense,
                Alpha = I("initialAlpha"),
                Scale = F("scale"), HitboxOverride = new(I("width"), I("height")),
                MoneyValue = F("value"), KnockBackResist = F("initialKnockback")
            });
        Assert.True(store.TrySpawn(150, in input, out var before));
        if (before.Simulation.Scale != F("scale"))
        {
            var actualSourceBody = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
                before.VelocityX, before.VelocityY, before.Target, before.Ai,
                before.Simulation with { Scale = F("scale") });
            Assert.True(store.TryUpdate(before.Handle, in actualSourceBody, out before));
        }
        var players = new Players
        {
            State = new(new(new(0), new(1)), new(1), 0, 0, 0, 0, 0, 0, 1100f, I("depth") * 16f,
                0, 0, 0, 0, 0, 0, 0, 0, 0)
        };
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.EnableBlueSlimeMotion(140d);
        targeting.SetWorldBounds(600, 140d, 200d, 500);
        targeting.SetWorldConditions(seed % 2 == 0, facts.SlimeRain, Good(), Good(), false, 0f,
            facts.RemixWorld, 0d, facts.NoTrapsWorld, false, facts.LowTiles, facts.NoHellstone,
            facts.NoLifeCrystals, true);
        targeting.SetPlayerSnapshotLookup(players);
        targeting.SetCandidates([new(0, 1110f, I("depth") * 16f + 21f, 0, true, false, false, false)]);
        targeting.SetSlimeContainedOwner(store, () => facts, new World());

        int selected = (int)row.GetProperty("ai")[1].GetSingle();
        bool unsupported = selected is 314 or 150 || selected == 8 && Good();
        var expectedUnchangedRandom = random.SourceRandom.Clone();
        bool proposed = targeting.TryStepState(in before, out var placeholder);
        if (unsupported)
        {
            Assert.False(proposed);
            Assert.True(random.SourceRandom.HasSameState(expectedUnchangedRandom));
            Assert.True(store.TryGet(before.Handle, out var unchanged));
            Assert.Equal(before, unchanged);
            Assert.Equal(1, store.ActiveCount);
            return;
        }

        Assert.True(proposed);
        Assert.True(store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
        Assert.True(targeting.TryGetSlimeContainedPlan(in before, in accepted, out var planned));
        var final = targeting.CompleteSlimeContainedPlan(in before, in accepted, in planned);
        Assert.True(final.IsActive);
        Assert.Equal(F("outX"), final.PositionX);
        Assert.Equal(F("outY"), final.PositionY);
        Assert.Equal(F("outVx"), final.VelocityX);
        Assert.Equal(F("outVy"), final.VelocityY);
        Assert.Equal(row.GetProperty("ai")[0].GetSingle(), final.Ai.Ai0);
        Assert.Equal(row.GetProperty("ai")[1].GetSingle(), final.Ai.Ai1);
        Assert.Equal(row.GetProperty("ai")[2].GetSingle(), final.Ai.Ai2);
        Assert.Equal(I("life"), final.Simulation.Life);
        Assert.Equal(I("lifeMax"), final.Simulation.LifeMax);
        Assert.Equal(I("damage"), final.Simulation.DamageOverride);
        Assert.Equal(I("defense"), final.Simulation.DefenseOverride);
        Assert.Equal(I("alpha"), final.Simulation.Alpha);
        Assert.Equal(F("outScale"), final.Simulation.Scale);
        Assert.True(definition.TryResolveHitbox(final.Simulation, out var body));
        Assert.Equal(I("outWidth"), body.Width);
        Assert.Equal(I("outHeight"), body.Height);
        Assert.Equal(I("direction"), final.Simulation.DirectionX);
        Assert.Equal(I("directionY"), final.Simulation.DirectionY);
        Assert.Equal(F("knockback"), final.Simulation.KnockBackResist);
        Assert.Equal(row.GetProperty("noTile").GetBoolean(), final.Simulation.NoTileCollide);
        var children = row.GetProperty("children");
        Assert.Equal(1 + children.GetArrayLength(), store.ActiveCount);
        foreach (var childRow in children.EnumerateArray())
        {
            Assert.True(store.TryGetActive((byte)childRow.GetProperty("slot").GetInt32(), out var child));
            Assert.Equal(childRow.GetProperty("type").GetInt32(), child.Type);
            Assert.Equal(childRow.GetProperty("x").GetSingle(), child.PositionX);
            Assert.Equal(childRow.GetProperty("y").GetSingle(), child.PositionY);
            Assert.Equal(childRow.GetProperty("vx").GetSingle(), child.VelocityX);
            Assert.Equal(childRow.GetProperty("vy").GetSingle(), child.VelocityY);
            Assert.Equal(childRow.GetProperty("ai")[1].GetSingle(), child.Ai.Ai1);
            Assert.Equal(childRow.GetProperty("local")[0].GetSingle(), child.Simulation.LocalAi.Ai0);
        }
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    private sealed class World : IVanillaSlimeContainedEnvironment1458, IVanillaSlimeContainedWorld1458
    {
        public bool IsCurrent => true;
        public bool CanHit => true;
        public bool TryCapture(in NpcSnapshot parent, in VanillaNpcTargetCandidate target, out IVanillaSlimeContainedWorld1458 world)
        { world = this; return true; }
        public bool TryReadBirthWet(in NpcSnapshot birth, out bool wet) { wet = false; return true; }
    }

    private sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        public PlayerStateSnapshot State;
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        { snapshot = State; return slot.Value == 0; }
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return false; }
    }
}
