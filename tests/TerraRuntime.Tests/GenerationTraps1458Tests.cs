using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationTraps1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationTraps1458Tests).Assembly.GetManifestResourceStream("GenerationTraps1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var data = Read(); foreach (string stage in new[] { "cavern", "sand" }) foreach (var row in data.RootElement.GetProperty(stage).EnumerateArray()) yield return new object[] { stage, row.GetProperty("id").GetInt32() }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Trap_assembly_matches_untouched_original_full_grid_random_return_and_retained_counter(string stage, int id)
    {
        using var data = Read(); var row = data.RootElement.GetProperty(stage).EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        bool[] phase = data.RootElement.GetProperty("phase").EnumerateArray().Select(x => x.GetBoolean()).ToArray(); int mode = row.GetProperty("mode").GetInt32();
        var workspace = stage == "cavern" ? CreateCavern(mode, phase) : CreateSand(mode, phase); var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        var trap = new GenerationTrapPlacement1458(workspace, random, 150, 550, stage == "cavern" && mode == 3 ? new(60, 350) : new(0, 0), 0);
        bool returned = stage == "cavern" ? trap.Place(60, 350, row.GetProperty("kind").GetInt32()) : trap.PlaceSand(80, row.GetProperty("probe").GetInt32());
        Assert.Equal(row.GetProperty("returned").GetBoolean(), returned);
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State());
        if (stage == "cavern") Assert.Equal(row.GetProperty("pets").GetInt32(), workspace.VanillaBoulderPetsPlaced);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    public void Trap_caller_matches_original_379_phase_and_full_physical_random(int seed)
    {
        using var data=Read();var row=data.RootElement.GetProperty("caller379").EnumerateArray().Single(x=>x.GetProperty("seed").GetInt32()==seed);
        var workspace=new Workspace(800,800);workspace.InitializeVanillaGenerationSolidity();int type=0;
        foreach(var value in data.RootElement.GetProperty("phase").EnumerateArray())workspace.SetVanillaGenerationSolid(new TileTypeId(type++),value.GetBoolean());
        for(int x=0;x<800;x++)for(int y=0;y<800;y++)workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x,y)]=new WorldTile{Type=379,Flags=WorldTileFlags.Active};
        workspace.TrySetLayers(150,250);workspace.SetVanillaLiquidLines(500,550);workspace.VanillaShimmerPosition=new(0,0);
        // The actual registered original delegate receives an explicit800x800 all379 context.
        // Setup metadata is independent of the pass's fresh measured physical RNG.
        var bootstrap=BootstrapPass1458.Run(new RandomAdapter(1458),4200,false);workspace.SetVanillaBootstrapState(bootstrap);
        var state=new SurfaceFinishState1458();typeof(SurfaceFinishState1458).GetProperty("Bootstrap")!.SetValue(state,bootstrap);
        typeof(SurfaceFinishState1458).GetProperty("WorldSurface")!.SetValue(state,150d);typeof(SurfaceFinishState1458).GetProperty("RockLayer")!.SetValue(state,250d);
        var random=new RandomAdapter(seed);var pass=new SurfaceFinishPass1458(SurfaceFinishStage1458.Traps,state);
        typeof(SurfaceFinishPass1458).GetMethod("ApplyTraps",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(pass,[new CallerContext(workspace,random,seed),workspace]);
        Assert.Equal(row.GetProperty("grid").GetString(),GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(row.GetProperty("cursor").GetUInt32()+":"+string.Join(',',row.GetProperty("slots").EnumerateArray().Select(x=>x.GetInt32())),random.State());
        Assert.Equal(row.GetProperty("solid379").GetBoolean(),workspace.IsVanillaGenerationSolid(new TileTypeId(379)));
        Assert.Equal(row.GetProperty("pets").GetInt32(),workspace.VanillaBoulderPetsPlaced);Assert.Equal(row.GetProperty("next").GetInt32(),random.Next());
    }
    private sealed class CallerContext(Workspace workspace,IWorldGenerationVanillaRandom random,int seed):IWorldGenerationContext
    {
        public WorldGenerationRequest Request=>new(Provider1458.GeneratorId,"Traps379",(ulong)seed,800,800){SeedText=seed.ToString(System.Globalization.CultureInfo.InvariantCulture),Options=new(WorldGenerationGameMode.Classic,WorldGenerationEvil.Corruption)};
        public IWorldGenerationWorkspace Workspace=>workspace;public IWorldGenerationMetadataWorkspace Metadata=>workspace;
        public IWorldGenerationRandom Random=>throw new NotSupportedException();public IWorldGenerationVanillaRandom VanillaRandom=>random;
        public CancellationToken CancellationToken=>Xunit.TestContext.Current.CancellationToken;
        public void ReportProgress(double fraction,string? message=null){}
    }

    private static Workspace CreateCavern(int mode, bool[] phase)
    {
        var w = new Workspace(120, 800); w.InitializeVanillaGenerationSolidity(); for (int i = 0; i < phase.Length; i++) w.SetVanillaGenerationSolid(new TileTypeId(i), phase[i]); w.SetVanillaGenerationSolid(new TileTypeId(379), false);
        for (int x = 0; x < 120; x++) for (int y = 370; y < 800; y++) if (mode != 1) At(x, y) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; for (int x = 54; x <= 66; x++) for (int y = 337; y <= 349; y++) At(x, y) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; foreach (int x in new[] { 48, 72 }) for (int y = 350; y <= 370; y++) At(x, y) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        if (mode == 2) At(60, 350).Wall = 350; if (mode == 4) { At(60, 369).LiquidAmount = 90; At(60, 369).LiquidKind = WorldLiquidKind.Lava; }
        if (mode == 5) At(60, 369).LiquidAmount = 90;
        if (mode == 6) At(60, 370).TrySetSourceShape(true, 0); if (mode == 7) At(60, 370).TrySetSourceShape(false, 3); if (mode == 8) At(60, 370).TrySetSourceShape(false, 1); if (mode == 9) At(60, 370).Flags |= WorldTileFlags.Inactive; if (mode == 10) At(60, 370).Type = 48; if (mode == 11) At(60, 370).Type = 232; if (mode == 12) At(60, 369) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.Inactive };
        if (mode is 13 or 14) At(60 + (mode == 13 ? 20 : 21), 369) = new WorldTile { Type = 70, Flags = WorldTileFlags.Active }; if (mode == 15) At(60, 369).Wall = 87; if (mode == 16) At(60, 369).Wall = 350; if (mode == 17) At(60, 370).Wall = 350;
        if (mode == 18) for (int x = 45; x <= 74; x++) for (int y = 330; y <= 379; y++) { ref var t = ref At(x, y); t.TileColor = 7; t.WallColor = 5; t.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow; }
        if (mode == 19) At(48, 368).Type = 190; if (mode == 20) for (int x = 58; x <= 62; x++) for (int y = 332; y <= 336; y++) At(x, y).Wall = 62; if (mode == 21) for (int x = 50; x <= 70; x += 10) At(x, 340).Wall = 7; if (mode == 22) for (int x = 58; x <= 62; x++) for (int y = 332; y <= 336; y++) { At(x, y).LiquidAmount = 90; At(x, y).LiquidKind = WorldLiquidKind.Lava; }
        if (mode == 23) for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++) { At(68 + dx, 346 + dy) = new WorldTile { Type = 138, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) }; At(68 + dx, 348) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; }
        w.VanillaBoulderPetsPlaced=mode==24?1:mode==25?2:mode==26?3:0;
        if(mode==27)At(60,346).Flags|=WorldTileFlags.Actuator;
        return w;
        ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private static Workspace CreateSand(int mode, bool[] phase)
    {
        var w = new Workspace(160, 800); w.InitializeVanillaGenerationSolidity(); for (int i = 0; i < phase.Length; i++) w.SetVanillaGenerationSolid(new TileTypeId(i), phase[i]); w.SetVanillaGenerationSolid(new TileTypeId(379), false);
        for (int x = 0; x < 160; x++) for (int y = 0; y < 800; y++) { At(x, y).Wall = 187; if (mode != 1 && y >= 390) { At(x, y).Flags |= WorldTileFlags.Active; At(x, y).Type = 397; } }
        int roof = mode == 9 ? 383 : 369, probe = mode == 9 ? 385 : 375; for (int x = 45; x <= 115; x++) for (int y = 335; y <= roof; y++) { At(x, y).Flags |= WorldTileFlags.Active; At(x, y).Type = 396; }
        if (mode == 2) At(80, 390).Type = 1; if (mode == 3) At(80, 390).TrySetSourceShape(true, 0); if (mode == 4) At(80, 390).TrySetSourceShape(false, 1); if (mode == 5) At(80, 390).TrySetSourceShape(false, 3); if (mode == 6) At(80, 390).Wall = 0;
        if (mode == 7) for (int y = 335; y <= 369; y++) At(80, y).Flags &= ~WorldTileFlags.Active; if (mode == 8) At(80, 369).Type = 1; if (mode == 10) At(80, 368).Type = 7; if (mode == 11) At(80, 368).Type = 404; if (mode == 12) At(79, 366).Flags &= ~WorldTileFlags.Active; if (mode == 13) { At(80, 379).Flags |= WorldTileFlags.Active; At(80, 379).Type = 1; }
        if (mode == 14) At(105, 413).Flags |= WorldTileFlags.WireRed; if (mode == 15) At(105, 414).Flags |= WorldTileFlags.WireRed; if (mode == 16) At(100, 400).Type = 21; if (mode == 17) At(100, 400).Type = 245;
        if (mode == 18) for (int x = 66; x <= 94; x++) for (int y = 350; y <= 389; y++) { ref var t = ref At(x, y); t.TileColor = 7; t.WallColor = 5; t.TrySetSourceShape(true, 3); t.Flags |= WorldTileFlags.Inactive | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow; }
        if (mode == 19) foreach (int x in new[] { 78, 79, 81, 82 }) { At(x, 389).Flags |= WorldTileFlags.Active; At(x, 389).Type = 3; }
        if (mode == 20) At(100, 400).Type = 19; if (mode == 21) At(100, 400).Type = 10; if (mode == 22) for (int x = 45; x <= 80; x++) for (int y = 350; y <= 368; y++) At(x, y).Flags &= ~WorldTileFlags.Active; if (mode == 23) for (int x = 78; x <= 82; x++) for (int y = 390; y <= 392; y++) At(x, y).Flags |= WorldTileFlags.Inactive;
        if(mode>=24){ref WorldTile guard=ref At(100,400);guard.Flags&=~WorldTileFlags.Active;guard.Type=(ushort)new[]{21,467,240,241,242,245,246,10,19,15,219,314}[mode-24];}
        return w;
        ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }

}
