using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using Xunit;
namespace TerraRuntime.Tests;

// Actual official GenerateWorld prefixes, ten basic profiles at all three canonical sizes.
// Captures precede each source report draw; all physical RNG slots and retained producers are checked.
public sealed class GenerationNaturalSpecialMid1458Tests
{
    private static JsonDocument Read()
    {
        using var stream = typeof(GenerationNaturalSpecialMid1458Tests).Assembly.GetManifestResourceStream("GenerationNaturalSpecialMid1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    public static IEnumerable<object[]> OriginalRows()
    {
        using var data = Read();
        for (int i = 0; i < data.RootElement.GetArrayLength(); i++)
            yield return new object[] { i };
    }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Continuous_basic_special_through_gems_matches_original_cells_random_and_retained_producers(int id)
    {
        using var data = Read();
        JsonElement row = data.RootElement[id];
        int width = row.GetProperty("width").GetInt32(), height = width == 4200 ? 1200 : width == 6400 ? 1800 : 2400;
        var workspace = new Workspace(width, height);
        // Actual clearWorld+Reset was also captured with these stale producer facts preloaded.
        if (id == 0)
        {
            workspace.AddVanillaLandmass(new(VanillaLandmassKind1458.SkyblockIsland, 50, 50, 21, 4, 7));
            workspace.MarkVanillaSpawnRandomized();
            workspace.SetVanillaGenerationOres(new(9, 9, 9, 9));
        }
        var npcs = new TerraRuntime.Core.Npcs.RuntimeNpcStore();
        var projectiles = new TerraRuntime.Core.Projectiles.RuntimeProjectileStore();
        var npcContext = new TerraRuntime.Gameplay.Npcs.VanillaNpcSpawnContext(1, 0, false) { WorldDifficulty = 1 };
        npcs.SetVanillaSpawnContextSource(() => npcContext);
        npcs.SetVanillaSpawnWetCollisionSource((x, y, w, h) => VanillaWorldCollision.GetLiquidContacts(workspace.TileStore, x, y, w, h).Wet);
        Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));

        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(provider.Id, "ActualSpecialMid", 0, width, height)
        {
            SeedText = row.GetProperty("text").GetString(),
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption)
        };
        var profile = WorldSeedResolver1458.Resolve(in request);
        bool good = profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) || profile.Has(VanillaSpecialWorldSeed1458.Zenith);
        float difficulty = good ? 2 : 1;
        npcContext = new(difficulty, 0, good)
        {
            WorldDifficulty = difficulty,
            TenthAnniversaryWorld = profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10) || profile.Has(VanillaSpecialWorldSeed1458.Zenith),
            RemixWorld = profile.Has(VanillaSpecialWorldSeed1458.Remix) || profile.Has(VanillaSpecialWorldSeed1458.Zenith)
        };
        workspace.AttachEntityEffects(new TerraRuntime.Core.Worlds.Generation.WorldGenerationEntityEffects1458(npcs, projectiles, sourceServer: true), sourceMyPlayer: 0, sourceNpcDifficulty: difficulty);
        using var cancellation = new CancellationTokenSource();
        var capture = new Capture(workspace, npcs, projectiles, row.GetProperty("boundaries"), row.GetProperty("profile").GetString() == "Skyblock", cancellation);
        var result = RuntimeWorldGenerationExecutor.Execute(new Provider(provider, capture), request, workspace, null, cancellation.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal("terraria:1.4.5.8/Gems", result.PassId.Value);
        Assert.Equal(36, capture.Count);
        Assert.Empty(workspace.CaptureGeneratedTileEntities());
    }
    private sealed class Provider(IWorldGenerationProvider provider, Capture capture) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => provider.Id;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) => provider.BuildPlan(in request, new Builder(builder, capture));
    }
    private sealed class Builder(IWorldGenerationPlanBuilder builder, Capture capture) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => builder.Add(descriptor, new Pass(descriptor, pass, capture));
    }
    private sealed class Pass(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass, Capture capture) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            pass.Execute(context);
            capture.After(descriptor, context, pass);
        }
    }
    private sealed class Capture(Workspace workspace, TerraRuntime.Core.Npcs.RuntimeNpcStore npcs, TerraRuntime.Core.Projectiles.RuntimeProjectileStore projectiles, JsonElement boundaries, bool skyblock, CancellationTokenSource cancellation)
    {
        public int Count
        {
            get; private set;
        }
        public void After(WorldGenerationPassDescriptor descriptor, IWorldGenerationContext context, IWorldGenerationPass pass)
        {
            if (descriptor.Id.Value is "terraria:1.4.5.8/Reset" or "terraria:1.4.5.8/TerrainLayers" or "terraria:1.4.5.8/Biomes" or "terraria:1.4.5.8/Caves" or "terraria:1.4.5.8/Ores" or "terraria:1.4.5.8/DualDungeonsDitherSnake")
                return;
            JsonElement expected = boundaries[Count++];
            Assert.Equal(expected.GetProperty("id").GetString(), descriptor.Id.Value);
            Assert.Equal(expected.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var random = (VanillaUnifiedRandom1458)context.VanillaRandom!.GetType().GetField("random", flags)!.GetValue(context.VanillaRandom)!;
            var cursorField = typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!;
            var slotsField = typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!;
            Assert.Equal(expected.GetProperty("cursor").GetUInt32(), (uint)cursorField.GetValue(random)!);
            Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()), (int[])slotsField.GetValue(random)!);
            var clone = new VanillaUnifiedRandom1458(0);
            cursorField.SetValue(clone, cursorField.GetValue(random));
            Array.Copy((int[])slotsField.GetValue(random)!, (int[])slotsField.GetValue(clone)!, 56);
            Assert.Equal(expected.GetProperty("next").GetInt32(), clone.Next());
            Assert.Equal(expected.GetProperty("phase").EnumerateArray().Select(x => x.GetBoolean()), Enumerable.Range(0, 754).Select(x => workspace.IsVanillaGenerationSolid(new(x))));
            Assert.True(workspace.TryGetLayers(out var layers));
            Assert.Equal(expected.GetProperty("surface").GetDouble(), layers.WorldSurface);
            Assert.Equal(expected.GetProperty("rock").GetDouble(), layers.RockLayer);
            Assert.True(ReferenceEquals(workspace.TileStore, typeof(VanillaStructureMap1458).GetField("<tiles>P", flags)!.GetValue(workspace.VanillaStructureMap)));
            Regions(expected.GetProperty("structures"), workspace.VanillaStructureMap.CaptureStructures());
            Regions(expected.GetProperty("protected"), workspace.VanillaStructureMap.CaptureProtectedStructures());
            Assert.Equal(expected.GetProperty("pyramids").EnumerateArray().Select(p => (p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32())), workspace.CaptureVanillaPyramidCandidates().Select(p => (p.X, p.Y)));
            var land = workspace.CaptureVanillaLandmasses();
            Assert.Equal(expected.GetProperty("landmasses").GetArrayLength(), land.Length);
            for (int i = 0; i < land.Length; i++)
            {
                JsonElement value = expected.GetProperty("landmasses")[i];
                Assert.Equal(value.GetProperty("DataType").GetInt32(), (int)land[i].Kind);
                Assert.Equal(value.GetProperty("Position").GetProperty("X").GetSingle(), land[i].X);
                Assert.Equal(value.GetProperty("Position").GetProperty("Y").GetSingle(), land[i].Y);
                Assert.Equal(value.GetProperty("RadiusOrHalfSize").GetInt32(), land[i].RadiusOrHalfSize);
                Assert.Equal(value.GetProperty("Style").GetInt32(), land[i].Style);
                Assert.Equal(value.GetProperty("ExtraData").GetInt32(), land[i].ExtraData);
            }
            Assert.Equal(expected.GetProperty("randomizedSpawn").GetBoolean(), workspace.VanillaSpawnHasBeenRandomized);
            if (workspace.VanillaSpawnHasBeenRandomized)
            {
                Assert.True(workspace.TryGetSpawn(out var spawn));
                Assert.Equal(expected.GetProperty("spawnX").GetInt32(), spawn.X);
                Assert.Equal(expected.GetProperty("spawnY").GetInt32(), spawn.Y);
            }
            if (expected.TryGetProperty("dungeon", out var dungeon))
            {
                DungeonSetupProfile1458 setup = workspace.VanillaDungeonSetupProfile!.Value;
                DungeonPalette1458 p = setup.Palette;
                Assert.Equal(dungeon.GetProperty("palette").EnumerateArray().Select(x => x.GetInt32()), new[] { p.Color, p.BrickTileType, p.BrickWallType, p.CrackedBrickTileType, p.WindowGlassWallType, p.WindowClosedGlassWallType, p.WindowEdgeWallType, p.WindowPlatformItemType });
                Assert.Equal(dungeon.GetProperty("kind").GetInt32(), (int)setup.EntranceKind);
                Assert.Equal(dungeon.GetProperty("seed").GetInt32(), setup.EntranceRandomSeed);
            }
            if (!skyblock && expected.TryGetProperty("tunnels", out var tunnels))
                Assert.Equal(tunnels.EnumerateArray().Select(x => x.GetInt32()), workspace.VanillaTunnelColumns.ToArray());
            if (expected.TryGetProperty("mountains", out var mountains))
                Assert.Equal(mountains.EnumerateArray().Select(p => (p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32())), workspace.VanillaMountainCaves.ToArray().Select(p => (p.X, p.Y)));
            if (expected.TryGetProperty("snow", out var snow))
            {
                Assert.Equal(snow.GetProperty("top").GetInt32(), workspace.VanillaSnowTop);
                Assert.Equal(snow.GetProperty("bottom").GetInt32(), workspace.VanillaSnowBottom);
                if (!skyblock)
                {
                    Assert.Equal(snow.GetProperty("left").EnumerateArray().Select(x => x.GetInt32()), workspace.VanillaSnowRows.ToArray().Select(x => x.Left));
                    Assert.Equal(snow.GetProperty("right").EnumerateArray().Select(x => x.GetInt32()), workspace.VanillaSnowRows.ToArray().Select(x => x.Right));
                }
            }
            var ores = workspace.VanillaGenerationOres;
            Assert.Equal(expected.GetProperty("ores").EnumerateArray().Select(x => x.GetInt32()), new[] { ores.Copper, ores.Iron, ores.Silver, ores.Gold });
            if (expected.TryGetProperty("desert", out var desert))
            {
                var region = workspace.VanillaUndergroundDesertRegion!.Value;
                var generated = workspace.VanillaDesertGenerationState!.Value;
                Rectangle(desert.GetProperty("region"), new(region.X, region.Y, region.Width, region.Height));
                Rectangle(desert.GetProperty("hive"), generated.Hive);
                Assert.Equal(desert.GetProperty("density").EnumerateArray().Select(x => x.GetInt32()), new[] { generated.DensityBounds.X, generated.DensityBounds.Y, generated.DensityBounds.Width, generated.DensityBounds.Height });
            }
            if (expected.TryGetProperty("mushrooms", out var mushrooms))
                Assert.Equal(mushrooms.EnumerateArray().Select(p => (p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32())), workspace.VanillaMushroomCenters.ToArray().Select(p => (p.X, p.Y)));
            if (expected.TryGetProperty("sky", out var sky))
                Assert.Equal(sky.EnumerateArray().Select(p => (p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32(), p.GetProperty("Style").GetInt32(), p.GetProperty("Lake").GetBoolean())), workspace.VanillaSkyIslands.ToArray().Select(p => (p.X, p.Y, p.Style, p.IsLake)));
            if (expected.TryGetProperty("dungeonCursor", out var sourceCursor))
            {
                var entrance = workspace.VanillaDungeonGraph!.Components.Single(c => c.Kind == DungeonComponentKind1458.Entrance);
                Assert.Equal((sourceCursor[0].GetInt32(), sourceCursor[1].GetInt32()), (entrance.Start.X, entrance.Start.Y));
                var state = (DungeonState1458)typeof(DungeonPass1458).GetField("state", flags)!.GetValue(pass)!;
                // This same retained value is consumed later by the pyramid placement gate.
                Assert.Equal(sourceCursor[0].GetInt32(), state.DungeonGenerationX);
            }
            Assert.Equal(expected.GetProperty("chests").GetInt32(), workspace.GeneratedChestCount);
            if (expected.TryGetProperty("chestRecords", out var chestRecords))
            {
                var chests = workspace.CaptureGeneratedChests();
                Assert.Equal(chestRecords.GetArrayLength(), chests.Length);
                for (int c = 0; c < chests.Length; c++)
                {
                    JsonElement source = chestRecords[c];
                    var chest = chests[c];
                    Assert.Equal(source.GetProperty("slot").GetInt32(), chest.SlotId);
                    Assert.Equal(source.GetProperty("x").GetInt32(), chest.X);
                    Assert.Equal(source.GetProperty("y").GetInt32(), chest.Y);
                    Assert.Equal(source.GetProperty("name").GetString(), chest.Name);
                    Assert.Equal(source.GetProperty("items").GetArrayLength(), chest.Items.Length);
                    for (int item = 0; item < chest.Items.Length; item++)
                    {
                        JsonElement value = source.GetProperty("items")[item];
                        var actual = chest.Items[item];
                        Assert.Equal((value.GetProperty("type").GetInt32(), value.GetProperty("stack").GetInt32(), value.GetProperty("prefix").GetInt32()), (actual.ItemType, actual.Stack, (int)actual.Prefix));
                    }
                }
            }

            Assert.Equal(expected.GetProperty("npcs").GetInt32(), npcs.ActiveCount);
            Assert.Equal(expected.GetProperty("projectiles").GetInt32(), projectiles.ActiveCount);
            if (expected.TryGetProperty("npcRecords", out var npcRecords))
                foreach (JsonElement source in npcRecords.EnumerateArray())
                {
                    Assert.True(npcs.TryGetActive(checked((byte)source.GetProperty("slot").GetInt32()), out var npc));
                    Assert.Equal(source.GetProperty("type").GetInt32(), npc.Type);
                    Assert.Equal(source.GetProperty("netID").GetInt32(), npc.NetId);
                    Assert.Equal(source.GetProperty("x").GetSingle(), npc.PositionX);
                    Assert.Equal(source.GetProperty("y").GetSingle(), npc.PositionY);
                    Assert.Equal(source.GetProperty("direction").GetInt32(), npc.Simulation.DirectionX);
                    Assert.Equal(source.GetProperty("spriteDirection").GetInt32(), npc.Simulation.SpriteDirection);
                    Assert.Equal(source.GetProperty("townNPC").GetBoolean(), npc.Simulation.TownNpc);
                    if (source.GetProperty("townNPC").GetBoolean())
                    {
                        Assert.Equal(source.GetProperty("homeless").GetBoolean(), npc.Simulation.Homeless);
                        Assert.Equal(source.GetProperty("homeTileX").GetInt32(), npc.Simulation.HomeTileX);
                        Assert.Equal(source.GetProperty("homeTileY").GetInt32(), npc.Simulation.HomeTileY);
                    }
                }
            if (expected.TryGetProperty("projectileRecords", out var projectileRecords))
                foreach (JsonElement source in projectileRecords.EnumerateArray())
                {
                    Assert.True(projectiles.TryGetActive(checked((ushort)source.GetProperty("slot").GetInt32()), out var projectile));
                    Assert.Equal(source.GetProperty("type").GetInt32(), projectile.Type.Value);
                    Assert.Equal(source.GetProperty("owner").GetInt32(), projectile.Spawner);
                    Assert.Equal(source.GetProperty("x").GetSingle(), projectile.PositionX);
                    Assert.Equal(source.GetProperty("y").GetSingle(), projectile.PositionY);
                    Assert.Equal(source.GetProperty("vx").GetSingle(), projectile.VelocityX);
                    Assert.Equal(source.GetProperty("vy").GetSingle(), projectile.VelocityY);
                    Assert.Equal(source.GetProperty("damage").GetInt32(), projectile.Damage);
                    Assert.Equal(source.GetProperty("originalDamage").GetInt32(), projectile.OriginalDamage);
                    Assert.Equal(source.GetProperty("knockBack").GetSingle(), projectile.KnockBack);
                    Assert.Equal(source.GetProperty("ai").EnumerateArray().Select(x => x.GetSingle()), new[] { projectile.Ai.Ai0, projectile.Ai.Ai1, projectile.Ai.Ai2 });
                }
            Assert.Equal(expected.GetProperty("tileEntities").GetInt32(), workspace.CaptureGeneratedTileEntities().Length);
            var liquids = workspace.TileStore.LiquidUpdates;
            Assert.Equal(expected.GetProperty("activeLiquids").GetInt32(), liquids.ActiveCount);
            Assert.Equal(expected.GetProperty("bufferedLiquids").GetInt32(), liquids.BufferedCount);
            Assert.Equal(0, expected.GetProperty("checkingLiquidCells").GetInt32());
            Assert.Equal(0, expected.GetProperty("skipLiquidCells").GetInt32());
            foreach (string name in new[] { "_activeMembership", "_bufferMembership", "_skipNextUpdate" })
                if (typeof(WorldLiquidUpdateQueue).GetField(name, flags)!.GetValue(liquids) is BitArray bits)
                {
                    var packed = new int[(bits.Length + 31) / 32];
                    bits.CopyTo(packed, 0);
                    Assert.All(packed, value => Assert.Equal(0, value));
                }
            if (Count == boundaries.GetArrayLength())
                cancellation.Cancel();
        }
        private static void Rectangle(JsonElement expected, WorldTileRegion actual) => Assert.Equal(
            (expected.GetProperty("X").GetInt32(), expected.GetProperty("Y").GetInt32(), expected.GetProperty("Width").GetInt32(), expected.GetProperty("Height").GetInt32()), (actual.X, actual.Y, actual.Width, actual.Height));
        private static void Regions(JsonElement expected, WorldTileRegion[] actual) => Assert.Equal(
            expected.EnumerateArray().Select(r => (r.GetProperty("X").GetInt32(), r.GetProperty("Y").GetInt32(), r.GetProperty("Width").GetInt32(), r.GetProperty("Height").GetInt32())), actual.Select(r => (r.X, r.Y, r.Width, r.Height)));
    }
}
