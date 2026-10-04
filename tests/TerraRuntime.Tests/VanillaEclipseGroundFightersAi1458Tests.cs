using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaEclipseGroundFightersAi1458Tests
{
    private static readonly ConcurrentDictionary<(int Width, int Height, bool Ground, bool Wall, bool Visible), WorldTileStore> Worlds = new();

    public static IEnumerable<object[]> Cases()
    {
        using var resource = typeof(VanillaEclipseGroundFightersAi1458Tests).Assembly
            .GetManifestResourceStream("EclipseGroundAi003Linux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Complete_AI_and_terrain_match_original_including_finite_rejection(JsonElement row)
    {
        float F(string name) => row.GetProperty(name).GetSingle();
        int I(string name) => row.GetProperty(name).GetInt32();
        bool B(string name) => row.GetProperty(name).GetBoolean();
        int type = I("type");
        var random = new SystemVanillaNpcRandom(I("seed"));
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        var tiles = Worlds.GetOrAdd((I("width"), I("height"), B("ground"), B("wallNear"), B("visible")), CreateWorld);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, tiles, 140d);
        stepper.SetWorldConditions(dayTime: B("day"), slimeRainActive: false, eclipseActive: B("eclipse"));
        stepper.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(tiles));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1000 + I("width") * .5f + F("dx"),
            1000 + I("height") * .5f + F("dy"), 0, true, false, false, false) { Stealth = 1f }]);
        int life = type == 162 ? 350 : type == 462 ? 270 : 600;
        var before = new NpcSnapshot(new NpcHandle(199, new NpcGeneration(1)), new NpcRevision(1),
            (short)type, (short)type, 1000, 1000, F("vx"), F("vy"), 0, new NpcAiState(0, 0, F("mode"), 0),
            NpcSimulationState.Initial with
            {
                Life = life,
                LifeMax = life,
                DirectionX = 1,
                DirectionY = 1,
                SpriteDirection = 1,
                OldPositionX = 999,
                OldPositionY = 1000,
                TimeLeft = 750,
                SpawnDifficulty = F("difficulty"),
                HitboxOverride = new NpcHitboxDimensions(I("width"), I("height")),
                JustHit = B("hit"),
                KnockBackResist = type == 162 ? .3f : type == 462 ? .7f : .35f
            });

        bool finite = row.GetProperty("outVx").ValueKind == JsonValueKind.Number &&
            row.GetProperty("outVy").ValueKind == JsonValueKind.Number;
        if (!finite)
        {
            Assert.False(stepper.TryStepState(in before, out _));
            Assert.Equal(I("nextRandom"), random.NextInt32(0, int.MaxValue));
            return;
        }

        Assert.True(stepper.TryStepState(in before, out var ai));
        if (type == 162)
        {
            var accepted = new NpcSnapshot(before.Handle, new NpcRevision(2), ai.Type, ai.NetId, ai.PositionX,
                ai.PositionY, ai.VelocityX, ai.VelocityY, ai.Target, ai.Ai, ai.Simulation);
            Span<NpcAiProjectileIntent> shots = stackalloc NpcAiProjectileIntent[5];
            Assert.True(stepper.TryPlanBeforeWorldMotion(in before, in accepted, shots, out int count, out ai));
            Assert.Equal(0, count);
        }
        Assert.True(world.TryApplyTerrainMotion(in before, in ai, out var next));
        Assert.Equal(F("outX"), next.PositionX);
        Assert.Equal(F("outY"), next.PositionY);
        Assert.Equal(F("outVx"), next.VelocityX);
        Assert.Equal(F("outVy"), next.VelocityY);
        var expectedAi = row.GetProperty("outAi");
        Assert.Equal(new NpcAiState(expectedAi[0].GetSingle(), expectedAi[1].GetSingle(),
            expectedAi[2].GetSingle(), expectedAi[3].GetSingle()), next.Ai);
        var expectedLocal = row.GetProperty("outLocal");
        Assert.Equal(new NpcAiState(expectedLocal[0].GetSingle(), expectedLocal[1].GetSingle(),
            expectedLocal[2].GetSingle(), expectedLocal[3].GetSingle()), next.Simulation.LocalAi);
        Assert.Equal(F("knockback"), next.Simulation.KnockBackResist);
        Assert.Equal(I("direction"), next.Simulation.DirectionX);
        Assert.Equal(I("directionY"), next.Simulation.DirectionY);
        Assert.Equal(I("sprite"), next.Simulation.SpriteDirection);
        Assert.Equal(I("target"), next.Target);
        Assert.Equal(I("timeLeft"), next.Simulation.TimeLeft);
        Assert.Equal(B("noGravity"), next.Simulation.NoGravity);
        Assert.Equal(I("nextRandom"), random.NextInt32(0, int.MaxValue));
    }

    [Fact]
    public void Canonical_names_preserve_species_and_distinct_AI22_admission()
    {
        Assert.Equal(82, VanillaNpcIds.Wraith.Value);
        Assert.Equal(122, VanillaNpcIds.Gastropod.Value);
        Assert.Equal(163, VanillaNpcIds.BlackRecluse.Value);
        Assert.Equal(166, VanillaNpcIds.SwampThing.Value);
        Assert.Equal(472, VanillaProjectileIds.WebSpit.Value);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Wraith, out var wraith));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Gastropod, out var gastropod));
        Assert.Equal(VanillaNpcBehaviorFamily.GhostHover, wraith.BehaviorFamily);
        Assert.Equal(VanillaNpcBehaviorFamily.GhostHover, gastropod.BehaviorFamily);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlackRecluse, out _));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SwampThing, out _));
    }

    private static WorldTileStore CreateWorld((int Width, int Height, bool Ground, bool Wall, bool Visible) key)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 500));
        int height = key.Height;
        if (key.Ground)
            for (int x = 60; x <= 80; x++)
                tiles.Set(x, (1000 + height + 7) / 16, new WorldTile
                { Type = (ushort)VanillaTileIds.Stone.Value, Flags = WorldTileFlags.Active });
        if (key.Wall)
            for (int x = (int)(1000 + key.Width * .5f) / 16 - 1; x <= (int)(1000 + key.Width * .5f) / 16 + 1; x++)
                for (int y = (int)(1000 + key.Height * .5f) / 16 - 1; y <= (int)(1000 + key.Height * .5f) / 16 + 1; y++)
                    tiles.Tiles[tiles.GetUncheckedIndex(x, y)].Wall = 1;
        if (!key.Visible)
            for (int y = 0; y < 500; y++)
                foreach (int x in new[] { 56, 70 })
                    tiles.Set(x, y, new WorldTile
                    { Type = (ushort)VanillaTileIds.Stone.Value, Flags = WorldTileFlags.Active });
        return tiles;
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
