using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationNaturalPrefix49Tests
{
    public static TheoryData<ulong, string, string, string, int, int, int, int> OriginalRows => new()
    {
        { 1458UL, "9D72933302F5495019BA9B08456022B9333DBF96EDDB1B7EA79F060C3A81270C", "112FB9158CFBE4E8903E797D91FE8ACA07C7275AFF28CE940DEE3FA7DAA308AF", "B806540A64AA05FBA326851DDE7AB1FC0FCA725FF79DB538B2D320497C577628", 25, 34, 11, 7 },
        { 42UL, "824847E48C62A08F81D1D48BD773B9550315DD5C5324594255CB9D695C748F40", "656770D5C5DCCD9438087C3F49E0E246AA209CE80976D8C585DCC3D52E78CFB1", "9F0E1DA4B5DB5E2326DE5C99B428DCF0B84299FCF20D5C23DF764174E7DFE838", 27, 30, 7, 8 },
    };

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Natural_prefix_through_jungle_shrines_matches_original_grid_rng_and_retained_inputs(
        ulong seed, string expectedGrid, string expectedRandom, string expectedMetadata,
        int chestCount, int protectedCount, int jungleChestCount, int larvaCount)
    {
        // Actual untouched official GenerateWorld: clearWorld/Reset plus all first49native passes.
        // The runtime has six additional compatibility entries before this boundary. Each actual pass and
        // its executor-owned per-pass RNG still run; cancellation stops the following liquid pass.
        var workspace = new Workspace(4200, 1200);
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(provider.Id, "NaturalPrefix", seed, 4200, 1200)
        {
            SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption)
        };
        using var cancel = new CancellationTokenSource();
        var capture = new Capture(workspace, cancel);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            new ObservedProvider(provider, capture), request, workspace, null, cancel.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal(SourceBackedJungleStructures1458.JungleChestsId, result.PassId);
        Assert.Equal(expectedGrid, capture.Grid);
        Assert.Equal(expectedRandom, capture.Random);
        Assert.Equal(expectedMetadata, capture.Metadata);
        Assert.Equal(chestCount, workspace.GeneratedChestCount);
        Assert.Equal(protectedCount, 1 + workspace.VanillaHiveProtectedAreas.Length + workspace.VanillaJungleShrineProtectedAreas.Length);
        Assert.Equal(jungleChestCount, workspace.VanillaJungleShrineChestAnchors.Length);
        Assert.Equal(larvaCount, workspace.VanillaHiveLarvaAnchors.Length);
    }

    private sealed class ObservedProvider(IWorldGenerationProvider provider, Capture capture) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => provider.Id;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            provider.BuildPlan(in request, new Builder(builder, capture));
    }
    private sealed class Builder(IWorldGenerationPlanBuilder builder, Capture capture) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            builder.Add(descriptor, new ObservedPass(descriptor, pass, capture));
    }
    private sealed class ObservedPass(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass, Capture capture) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            pass.Execute(context);
            if (descriptor.Id == SourceBackedJungleStructures1458.JungleChestsId) capture.Take(context);
        }
    }
    private sealed class Capture(Workspace workspace, CancellationTokenSource cancel)
    {
        public string Grid { get; private set; } = string.Empty;
        public string Random { get; private set; } = string.Empty;
        public string Metadata { get; private set; } = string.Empty;
        public void Take(IWorldGenerationContext context)
        {
            Grid = GridHash(workspace);
            Random = RandomHash(context.VanillaRandom!);
            Metadata = MetadataHash(workspace);
            cancel.Cancel();
        }
    }

    internal static string GridHash(Workspace workspace)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] column = new byte[workspace.HeightTiles * 17];
        WorldTileFlags[] masks = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
            WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
            WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
            WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        int unknown = 0, invalid = 0, reserved = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile t = workspace.TileStore.Get(x, y);
                Span<byte> b = column.AsSpan(y * 17, 17);
                b[0] = (byte)(t.IsActive ? 1 : 0);
                BinaryPrimitives.WriteUInt16LittleEndian(b[1..], t.Type);
                BinaryPrimitives.WriteUInt16LittleEndian(b[3..], t.Wall);
                BinaryPrimitives.WriteInt16LittleEndian(b[5..], t.FrameX);
                BinaryPrimitives.WriteInt16LittleEndian(b[7..], t.FrameY);
                b[9] = t.LiquidAmount; b[10] = (byte)t.LiquidKind; b[11] = (byte)(t.IsHalfBrick ? 1 : 0);
                b[12] = t.SourceSlope; b[13] = t.TileColor; b[14] = t.WallColor;
                ushort flags = 0;
                for (int bit = 0; bit < masks.Length; bit++) if ((t.Flags & masks[bit]) != 0) flags |= (ushort)(1 << bit);
                BinaryPrimitives.WriteUInt16LittleEndian(b[15..], flags);
                if ((t.Flags & ~WorldTileFlagMasks.Known) != 0) unknown++;
                if (!t.HasValidSourceShape) invalid++;
                if (t.Reserved != 0) reserved++;
            }
            hash.AppendData(column);
        }
        Assert.Equal(0, unknown); Assert.Equal(0, invalid); Assert.Equal(0, reserved);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static string RandomHash(IWorldGenerationVanillaRandom adapter, bool includeReport = true)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var original = (VanillaUnifiedRandom1458)adapter.GetType().GetField("random", flags)!.GetValue(adapter)!;
        var cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!;
        var slots = typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!;
        var clone = new VanillaUnifiedRandom1458(0);
        cursor.SetValue(clone, cursor.GetValue(original));
        Array.Copy((int[])slots.GetValue(original)!, (int[])slots.GetValue(clone)!, 56);
        if (includeReport) clone.Next(); // Official RunPass report, on a detached clone only.
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write((uint)cursor.GetValue(clone)!);
        foreach (int slot in (int[])slots.GetValue(clone)!) writer.Write(slot);
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static string MetadataHash(Workspace workspace)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        writer.Write(chests.Length);
        foreach (WorldChest chest in chests)
        {
            writer.Write((int)chest.SlotId); writer.Write(chest.X); writer.Write(chest.Y);
            byte[] name = Encoding.UTF8.GetBytes(chest.Name); writer.Write(name.Length); writer.Write(name);
            writer.Write(chest.Items.Length);
            foreach (WorldChestItem item in chest.Items)
            { writer.Write(item.ItemType); writer.Write(item.Stack); writer.Write((int)item.Prefix); }
        }
        var regions = new List<WorldTileRegion> { workspace.VanillaShimmerStructure!.Value };
        foreach (WorldTileRegion r in workspace.VanillaHiveProtectedAreas) regions.Add(r);
        foreach (WorldTileRegion r in workspace.VanillaJungleShrineProtectedAreas) regions.Add(r);
        writer.Write(regions.Count);
        foreach (WorldTileRegion r in regions) { writer.Write(r.X); writer.Write(r.Y); writer.Write(r.Width); writer.Write(r.Height); }
        void Points(ReadOnlySpan<WorldGenerationPoint> points)
        {
            writer.Write(points.Length);
            foreach (WorldGenerationPoint p in points) { writer.Write(p.X); writer.Write(p.Y); }
        }
        Points(workspace.VanillaJungleShrineChestAnchors);
        Points(workspace.VanillaHiveLarvaAnchors);
        Points(workspace.VanillaMountainCaves);
        var temple = workspace.VanillaTemplePart2State!.Value;
        var altar = workspace.VanillaLihzahrdAltarState!.Value;
        writer.Write(temple.Left); writer.Write(temple.Right); writer.Write(temple.Top); writer.Write(temple.Bottom);
        writer.Write(temple.Rooms); writer.Write(altar.X); writer.Write(altar.Y);
        var shimmer = workspace.VanillaShimmerPosition!.Value;
        var lines = workspace.VanillaLiquidLines!.Value;
        writer.Write(shimmer.X); writer.Write(shimmer.Y); writer.Write(lines.WaterLine); writer.Write(lines.LavaLine);
        Assert.True(workspace.TryGetLayers(out WorldGenerationLayers layers));
        writer.Write(layers.WorldSurface); writer.Write(layers.RockLayer);
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
}
