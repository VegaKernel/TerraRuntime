using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkeletronHandsSpawnTests
{
    public static TheoryData<int> SpawnCases => new(Enumerable.Range(0, 158));

    [Theory]
    [MemberData(nameof(SpawnCases))]
    public void Head_and_hand_spawns_match_original_context(int index)
    {
        using var resource = typeof(SkeletronHandsSpawnTests).Assembly.GetManifestResourceStream(OperatingSystem.IsWindows() ? "SkeletronSpawnWindows1458" : "SkeletronSpawn1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(OperatingSystem.IsWindows() ? "a8f880e367de0fcfcb6ee567e4414ffaad105a8c903be43806274f3834c02cc4" : "ab73933ca07f98061c01f3fedd4255fec4457cde6fcb91ceaa32ed56aa1e1e1d",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        var row = json.RootElement[index];
        NpcSpawnContextTests.AssertSpawn(row, row.TryGetProperty("requestedDifficulty", out var value) ? value.GetSingle() : null);
    }

    public static TheoryData<int, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, bool>();
            for (int index = 0; index < 48; index++)
            {
                cases.Add(index, false);
                cases.Add(index, true);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Initial_hand_batch_matches_original_before_children_run(int index, bool worldMotion)
    {
        using var resource = typeof(SkeletronHandsSpawnTests).Assembly.GetManifestResourceStream("SkeletronHandsSpawn1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("1e23a50698e96e548d4cd2c4bc4fe69e92f83cd351f668ca5b1bd680b903eabc",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        var row = json.RootElement[index];
        int mode = row.GetProperty("mode").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(mode + 1 + (good ? 1 : 0), 1, good));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { StartSlot = row.GetProperty("root").GetByte() }, out var head));
        if (row.GetProperty("fractional").GetBoolean())
        {
            var update = new NpcStateUpdate(head.Type, head.NetId, -0.75f, -0.75f, 0, 0, 0, head.Ai, head.Simulation);
            Assert.True(store.TryUpdate(head.Handle, in update, out head));
        }
        Assert.Equal(row.GetProperty("beforeX").GetSingle(), head.PositionX);
        Assert.Equal(row.GetProperty("beforeY").GetSingle(), head.PositionY);
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetWorldConditions(false, false, goodWorld: good,
            expertMode: mode >= 1 || good, masterMode: mode == 2 || (mode == 1 && good));
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        vanilla.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(tiles));
        INpcAiStateStepper stepper = worldMotion
            ? new VanillaNpcWorldMotionAiStepper(vanilla, tiles)
            : vanilla;
        var projectiles = new RuntimeProjectileStore();
        var executor = new RuntimeNpcAiStateExecutor(store, projectiles);
        var headOnly = new HeadOnly(stepper);
        Assert.Equal(1, executor.Tick(headOnly).Applied);
        Assert.True(store.TryGet(head.Handle, out var after));
        Assert.Equal(Ai(row.GetProperty("headAi")), after.Ai);
        Assert.Equal(row.GetProperty("headDamage").GetInt32(), after.Simulation.DamageOverride);
        Assert.Equal(row.GetProperty("headDefense").GetInt32(), after.Simulation.DefenseOverride);
        Assert.Equal(row.GetProperty("headVx").GetSingle(), after.VelocityX);
        Assert.Equal(row.GetProperty("headVy").GetSingle(), after.VelocityY);
        Assert.Equal(row.GetProperty("projectileCount").GetInt32(), projectiles.ActiveCount);
        var expected = row.GetProperty("children");
        Assert.Equal(expected.GetArrayLength() + 1, store.ActiveCount);
        foreach (var child in expected.EnumerateArray())
        {
            Assert.True(store.TryGetActive(child.GetProperty("slot").GetByte(), out var actual));
            Assert.Equal(child.GetProperty("type").GetInt32(), actual.Type);
            Assert.Equal(Ai(child.GetProperty("ai")), actual.Ai);
            Assert.Equal(Ai(child.GetProperty("local")), actual.Simulation.LocalAi);
            Assert.Equal(child.GetProperty("target").GetInt32(), actual.Target);
            Assert.Equal(child.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(child.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(child.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(child.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(child.GetProperty("lifeMax").GetInt32(), actual.Simulation.LifeMax);
            Assert.Equal(child.GetProperty("scale").GetSingle(), actual.Simulation.Scale);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(actual.TypeIdentity, out var definition));
            Assert.True(definition.TryResolveHitbox(actual.Simulation, out var hitbox));
            Assert.Equal(child.GetProperty("width").GetInt32(), hitbox.Width);
            Assert.Equal(child.GetProperty("height").GetInt32(), hitbox.Height);
        }
        // Even a partially created batch is initialized once, without retrying on the next head call.
        Assert.Equal(1, executor.Tick(headOnly).Applied);
        Assert.Equal(expected.GetArrayLength() + 1, store.ActiveCount);
    }

    private static NpcAiState Ai(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle());

    private sealed class HeadOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return npc.TypeIdentity == VanillaNpcIds.SkeletronHead && inner.TryStepState(in npc, out next);
        }
    }
}
