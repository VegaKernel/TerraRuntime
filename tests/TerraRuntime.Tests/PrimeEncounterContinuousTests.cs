using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>Continuous source trace for the first authoritative Skeletron Prime encounter tick.</summary>
public sealed class PrimeEncounterContinuousTests
{
    private static readonly JsonElement[] Rows = Read();

    [Fact]
    public void Head_and_arms_match_the_first_sixteen_continuous_encounter_ticks()
    {
        RuntimeNpcStore? npcs = null;
        RuntimeNpcAiStateExecutor? executor = null;
        INpcAiStateStepper? motion = null;
        foreach (JsonElement row in Rows)
        {
            if (row.GetProperty("tick").GetInt32() == 0)
                (npcs, executor, motion) = Create(row);

            Assert.NotNull(npcs); Assert.NotNull(executor); Assert.NotNull(motion);
            Assert.Equal(5, executor.Tick(motion).Applied);
            foreach (JsonElement expected in row.GetProperty("after").EnumerateArray())
                AssertNpc(npcs, expected);
        }
    }

    private static (RuntimeNpcStore Npcs, RuntimeNpcAiStateExecutor Executor, INpcAiStateStepper Motion) Create(JsonElement row)
    {
        var npcs = new RuntimeNpcStore();
        npcs.SetVanillaSpawnContextSource(() => new VanillaNpcSpawnContext(1, 1, false));
        Assert.True(npcs.TrySpawnIntent(new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronPrime, 1000, 1000, 0f, 0f, 0)
        {
            StartSlot = 0,
            InitialAi = new NpcAiState(0f, row.GetProperty("phase").GetSingle(), row.GetProperty("timer").GetSingle(), 0f)
        }, out _));

        var targeting = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        targeting.SetWorldConditions(dayTime: false, slimeRainActive: false);
        targeting.SetCandidates([new VanillaNpcTargetCandidate(0, 1510f, 1021f, 0, true, false, false, false)]);
        var motion = new VanillaNpcWorldMotionAiStepper(
            targeting, new WorldTileStore(new WorldDimensions(400, 400)));
        return (npcs, new RuntimeNpcAiStateExecutor(npcs), motion);
    }

    private static void AssertNpc(RuntimeNpcStore npcs, JsonElement expected)
    {
        Assert.True(npcs.TryGetActive(expected.GetProperty("slot").GetByte(), out var actual));
        Assert.Equal(expected.GetProperty("type").GetInt32(), actual.Type);
        Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("target").GetInt32(), actual.Target);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("directionY").GetInt32(), actual.Simulation.DirectionY);
        Assert.Equal(expected.GetProperty("spriteDirection").GetInt32(), actual.Simulation.SpriteDirection);
        float expectedRotation = expected.GetProperty("rotation").GetSingle();
        if (OperatingSystem.IsWindows())
            Assert.InRange(actual.Simulation.Rotation ?? 0f, expectedRotation - .000001f, expectedRotation + .000001f);
        else
            Assert.Equal(expectedRotation, actual.Simulation.Rotation);
        Assert.Equal(expected.GetProperty("damage").GetInt32(), actual.Simulation.DamageOverride ?? actual.Simulation.BaseDamage);
        Assert.Equal(expected.GetProperty("defense").GetInt32(), actual.Simulation.DefenseOverride ?? actual.Simulation.BaseDefense);
        Assert.Equal(ReadAi(expected.GetProperty("ai")), actual.Ai);
        Assert.Equal(ReadAi(expected.GetProperty("localAi")), actual.Simulation.LocalAi);
    }

    private static NpcAiState ReadAi(JsonElement values) => new(
        values[0].GetSingle(), values[1].GetSingle(), values[2].GetSingle(), values[3].GetSingle());

    private static JsonElement[] Read()
    {
        string name = OperatingSystem.IsWindows() ? "PrimeEncounterWindows1458" : "PrimeEncounterLinux1458";
        string hash = OperatingSystem.IsWindows()
            ? "f82ce034105fe491786737d47c82fa17fcbd3efa20f7834c42f3ccf9811c6587"
            : "6c39987475e3c515d6a2177fb49b4532ba40bc2aaf984c8221b4eb84d2ce4800";
        using var resource = typeof(PrimeEncounterContinuousTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
