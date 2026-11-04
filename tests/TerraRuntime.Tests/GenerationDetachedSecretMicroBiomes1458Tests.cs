using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds.Generation;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

/// <summary>Explicit source97 checkpoint imports; these12rows do not assert a continuous runtime prefix.</summary>
public sealed class GenerationDetachedSecretMicroBiomes1458Tests
{
    [Theory]
    [InlineData(1458,"MolePeople")]
    [InlineData(1458,"IAmError")]
    [InlineData(1458,"MoreTrapsPlease")]
    [InlineData(1458,"RainbowRoad")]
    [InlineData(1458,"MoleError")]
    [InlineData(1458,"ActuallyNoTrapsCount4")]
    [InlineData(42,"MolePeople")]
    [InlineData(42,"IAmError")]
    [InlineData(42,"MoreTrapsPlease")]
    [InlineData(42,"RainbowRoad")]
    [InlineData(42,"MoleError")]
    [InlineData(42,"ActuallyNoTrapsCount4")]
    public void Actual_registered_micro_pass_matches_whole_tiles_post_report_rng_chests_structures_and_effects(int seed,string profile)
    {
        using var json=JsonDocument.Parse(Resource($"checkpoint97-{profile}-{seed}.json.gz"));var input=json.RootElement;
        var gen=input.GetProperty("genVars");var world=new Workspace(4200,1200);world.InitializeVanillaStructureMap();world.InitializeVanillaGenerationSolidity();
        Assert.True(world.TrySetLayers(input.GetProperty("result").GetProperty("worldSurface").GetDouble(),input.GetProperty("result").GetProperty("rockLayer").GetDouble()));
        var shimmer=gen.GetProperty("shimmerPosition");world.VanillaShimmerPosition=new(shimmer.GetProperty("X").GetInt32(),shimmer.GetProperty("Y").GetInt32());
        int type=0;foreach(var solid in input.GetProperty("expectedSolidity").EnumerateArray())world.SetVanillaGenerationSolid(new(type++),solid.GetBoolean());
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.None,profile switch{"MolePeople"=>VanillaSecretWorldSeed1458.MolePeople,"IAmError"=>VanillaSecretWorldSeed1458.IAmError,"MoreTrapsPlease"=>VanillaSecretWorldSeed1458.MoreTrapsPlease,"RainbowRoad"=>VanillaSecretWorldSeed1458.RainbowRoad,"MoleError"=>VanillaSecretWorldSeed1458.MolePeople|VanillaSecretWorldSeed1458.IAmError,"ActuallyNoTrapsCount4"=>VanillaSecretWorldSeed1458.MoreTrapsPlease|VanillaSecretWorldSeed1458.MolePeople|VanillaSecretWorldSeed1458.RainbowRoad|VanillaSecretWorldSeed1458.NegativeInfinity,_=>throw new ArgumentException("Unknown configured extra case")}));
        Assert.True(world.TileStore.TryAttachTileEntities([],1000));
        using(var stream=Resource($"checkpoint97-{profile}-{seed}.bin.gz"))using(var reader=new BinaryReader(stream))foreach(ref var t in world.TileStore.Tiles)
        {
            bool active=reader.ReadByte()!=0;t.Type=reader.ReadUInt16();t.Wall=reader.ReadUInt16();t.FrameX=reader.ReadInt16();t.FrameY=reader.ReadInt16();
            t.LiquidAmount=reader.ReadByte();t.LiquidKind=(WorldLiquidKind)reader.ReadByte();bool half=reader.ReadByte()!=0;byte slope=reader.ReadByte();t.TileColor=reader.ReadByte();t.WallColor=reader.ReadByte();ushort flags=reader.ReadUInt16();
            if(active)t.Flags|=WorldTileFlags.Active;Assert.True(t.TrySetSourceShape(half,slope));
            for(int bit=0;bit<10;bit++)if((flags&(1<<bit))!=0)t.Flags|=Masks[bit];
        }
        var chestOwner=(List<WorldChest>)typeof(Workspace).GetField("generatedChests",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(world)!;
        foreach(var chest in input.GetProperty("chests").EnumerateArray())chestOwner.Add(ReadChest(chest));
        world.VanillaStructureMap.ImportCheckpoint(Regions(input.GetProperty("allStructures")),Regions(input.GetProperty("protectedRects")));
        var npcs=new RuntimeNpcStore();var projectiles=new RuntimeProjectileStore();
        npcs.SetVanillaSpawnContextSource(()=>new VanillaNpcSpawnContext(1,0,world.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy)){WorldDifficulty=1,RemixWorld=world.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.Remix)});
        npcs.SetVanillaSpawnWetCollisionSource((x,y,width,height)=>VanillaWorldCollision.GetLiquidContacts(world.TileStore,x,y,width,height).Wet);
        world.AttachEntityEffects(new WorldGenerationEntityEffects1458(npcs,projectiles,true),0,1);
 foreach(var projectile in input.GetProperty("projectiles").EnumerateArray()){
  var ai=projectile.GetProperty("ai");var update=new ProjectileStateUpdate(new(projectile.GetProperty("type").GetInt32()),projectile.GetProperty("owner").GetByte(),projectile.GetProperty("x").GetSingle(),projectile.GetProperty("y").GetSingle(),projectile.GetProperty("vx").GetSingle(),projectile.GetProperty("vy").GetSingle(),new(ai[0].GetSingle(),ai[1].GetSingle(),ai[2].GetSingle()),0,projectile.GetProperty("damage").GetInt16(),projectile.GetProperty("knockBack").GetSingle(),projectile.GetProperty("originalDamage").GetInt16());
  Assert.True(projectiles.TrySpawnVanilla(in update,projectile.GetProperty("timeLeft").GetInt32(),out var born,projectile.GetProperty("allFields").GetProperty("netUpdate").GetBoolean()));Assert.True(born.Handle.Slot==projectile.GetProperty("slot").GetUInt16());
 }
 foreach(var npc in input.GetProperty("npcs").EnumerateArray()) {
  var fields=npc.GetProperty("allFields");
  // Explicit checkpoint hydration through the existing Core slot API; this is NOT a special town creation/name binding.
  var sourceAi=fields.GetProperty("ai");
  var simulation=TerraRuntime.Contracts.Runtime.NpcSimulationState.Initial with{TimeLeft=fields.GetProperty("timeLeft").GetInt32(),Life=fields.GetProperty("life").GetInt32(),LifeMax=fields.GetProperty("lifeMax").GetInt32(),Homeless=npc.GetProperty("homeless").GetBoolean(),HomeTileX=npc.GetProperty("homeTileX").GetInt32(),HomeTileY=npc.GetProperty("homeTileY").GetInt32(),TownNpc=npc.GetProperty("townNPC").GetBoolean(),Friendly=fields.GetProperty("friendly").GetBoolean(),HitboxOverride=new(fields.GetProperty("width").GetInt32(),fields.GetProperty("height").GetInt32()),SpriteDirection=npc.GetProperty("spriteDirection").GetInt32(),DirectionX=npc.GetProperty("direction").GetInt32(),DirectionY=fields.GetProperty("directionY").GetInt32()};
  var update=new NpcStateUpdate(npc.GetProperty("type").GetInt32(),(short)npc.GetProperty("type").GetInt32(),npc.GetProperty("x").GetSingle(),npc.GetProperty("y").GetSingle(),0,0,fields.GetProperty("target").GetUInt16(),new(sourceAi[0].GetSingle(),sourceAi[1].GetSingle(),sourceAi[2].GetSingle(),sourceAi[3].GetSingle()),simulation);
  Assert.True(npcs.TrySpawn(npc.GetProperty("slot").GetByte(),in update,out var birth));
  var projection=new WorldTownNpc(npc.GetProperty("type").GetInt32(),npc.GetProperty("GivenName").GetString()!,npc.GetProperty("x").GetSingle(),npc.GetProperty("y").GetSingle(),npc.GetProperty("homeless").GetBoolean(),npc.GetProperty("homeTileX").GetInt32(),npc.GetProperty("homeTileY").GetInt32(),fields.GetProperty("townNpcVariationIndex").GetInt32(),fields.GetProperty("homelessDespawn").GetBoolean());
  const BindingFlags privateInstance=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  var projectionType=typeof(Workspace).GetNestedType("GeneratedTownNpcProjection",BindingFlags.NonPublic)!;
  var entry=projectionType.GetConstructors(privateInstance).Single(c=>c.GetParameters().Length==2).Invoke([projection,birth.Handle]);
  ((System.Collections.IList)typeof(Workspace).GetField("generatedTownNpcs",privateInstance)!.GetValue(world)!).Add(entry);

 }
        int[] hell=gen.GetProperty("hellChestItem").EnumerateArray().Select(i=>i.GetInt32()).ToArray();
        world.SetVanillaBootstrapState(new(){HellChestItems=hell,TreeX=[],TreeStyle=[],CaveBackX=[],CaveBackStyle=[],ForestBackgroundStyles=[],CopperBar=Int("copperBar"),IronBar=Int("ironBar"),SilverBar=Int("silverBar"),GoldBar=Int("goldBar")});
        var context=new BuriedChestContext1458{Height=1200,WorldSurface=input.GetProperty("result").GetProperty("worldSurface").GetDouble(),RockLayer=input.GetProperty("result").GetProperty("rockLayer").GetDouble(),LavaLine=Int("lavaLine"),
            CopperBar=Int("copperBar"),IronBar=Int("ironBar"),SilverBar=Int("silverBar"),GoldBar=Int("goldBar"),TungstenIsSilverTier=Int("silverBar")==705,DesertHiveLow=Int("desertHiveLow"),DesertHiveHigh=Int("desertHiveHigh"),HellChestItem=hell,
            JungleItemCount=Int("JungleItemCount"),HellChest=Int("hellChest"),GeneratedShadowKey=Bool("generatedShadowKey"),GeneratedRamRune=Bool("generatedRamRune"),GennedLivingMahoganyWands=Bool("gennedLivingMahoganyWands")};
        world.SetVanillaBuriedChestContext(context);var random=new SharedRandom(seed);
        world.SetVanillaTerrainState(new(context.WorldSurface,context.RockLayer,gen.GetProperty("worldSurface").GetDouble(),gen.GetProperty("rockLayer").GetDouble(),gen.GetProperty("worldSurfaceLow").GetDouble(),gen.GetProperty("worldSurfaceHigh").GetDouble(),gen.GetProperty("rockLayerLow").GetDouble(),gen.GetProperty("rockLayerHigh").GetDouble()));
        world.SetVanillaLiquidLines(0,Int("lavaLine"));
        new MicroBiomesPass1458().Execute(new SourceContext(world,random,seed,fraction=>
        {
            // Additional instrumented-original checkpoint, control-validated against the untouched whole pass.
            // It observes the first-offer rule before a counterfactual can enter an unrelated dust frontier.
            if (Math.Abs(fraction - .3) < 1e-9 && input.TryGetProperty("expectedSwordBoundary",out var sword))
            {
                const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
                Assert.Equal(sword.GetProperty("rngCursor").GetUInt32(),(uint)typeof(VanillaUnifiedRandom1458).GetField("inext",hidden)!.GetValue(random.Source)!);
                Assert.Equal(sword.GetProperty("rngSlots").EnumerateArray().Select(v=>v.GetInt32()),(int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",hidden)!.GetValue(random.Source)!);
                Assert.Equal(sword.GetProperty("sha256").GetString(),TileHash(world));
            }
        }));
        random.Next(); // Actual WorldGenerator report consumes its diagnostic draw after the registered pass.
        var expected=input.GetProperty("expectedResult");Assert.Equal(expected.GetProperty("sha256").GetString(),TileHash(world));
        const BindingFlags reflection=BindingFlags.NonPublic|BindingFlags.Instance;
        Assert.Equal(expected.GetProperty("rngCursor").GetUInt32(),(uint)typeof(VanillaUnifiedRandom1458).GetField("inext",reflection)!.GetValue(random.Source)!);
        Assert.Equal(expected.GetProperty("rngSlots").EnumerateArray().Select(v=>v.GetInt32()),(int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",reflection)!.GetValue(random.Source)!);
        Assert.Equal(Regions(input.GetProperty("expectedAllStructures")),world.VanillaStructureMap.CaptureStructures());Assert.Equal(Regions(input.GetProperty("expectedProtectedRects")),world.VanillaStructureMap.CaptureProtectedStructures());
        var actualChests=world.CaptureGeneratedChests();int chestIndex=0;
        foreach(var source in input.GetProperty("expectedChests").EnumerateArray())
        {var chest=ReadChest(source);var actual=actualChests[chestIndex++];Assert.Equal(chest.SlotId,actual.SlotId);Assert.Equal(chest.X,actual.X);Assert.Equal(chest.Y,actual.Y);Assert.Equal(chest.Name,actual.Name);Assert.Equal(chest.Items,actual.Items);}
        Assert.Equal(chestIndex,actualChests.Length);
        var counters=input.GetProperty("expectedCounters");Assert.Equal(counters.GetProperty("JungleItemCount").GetInt32(),context.JungleItemCount);Assert.Equal(counters.GetProperty("hellChest").GetInt32(),context.HellChest);
        Assert.Equal(counters.GetProperty("generatedShadowKey").GetBoolean(),context.GeneratedShadowKey);Assert.Equal(counters.GetProperty("generatedRamRune").GetBoolean(),context.GeneratedRamRune);Assert.Equal(counters.GetProperty("gennedLivingMahoganyWands").GetBoolean(),context.GennedLivingMahoganyWands);
        Assert.Equal(input.GetProperty("expectedProjectiles").GetArrayLength(),projectiles.ActiveCount);
        foreach(var projectile in input.GetProperty("expectedProjectiles").EnumerateArray())
        {
            Assert.True(projectiles.TryGetActive(projectile.GetProperty("slot").GetUInt16(),out var actual));Assert.Equal(projectile.GetProperty("type").GetInt32(),actual.Type.Value);Assert.Equal(projectile.GetProperty("owner").GetByte(),actual.Spawner);
            Assert.Equal(projectile.GetProperty("x").GetSingle(),actual.PositionX);Assert.Equal(projectile.GetProperty("y").GetSingle(),actual.PositionY);Assert.Equal(projectile.GetProperty("vx").GetSingle(),actual.VelocityX);Assert.Equal(projectile.GetProperty("vy").GetSingle(),actual.VelocityY);
            Assert.Equal(projectile.GetProperty("damage").GetInt16(),actual.Damage);Assert.Equal(projectile.GetProperty("originalDamage").GetInt16(),actual.OriginalDamage);Assert.Equal(projectile.GetProperty("knockBack").GetSingle(),actual.KnockBack);
            Assert.True(projectiles.TryGetLifecycle(actual.Handle,out var lifecycle));Assert.Equal(projectile.GetProperty("timeLeft").GetInt32(),lifecycle.TimeLeft);Assert.Equal(projectile.GetProperty("allFields").GetProperty("netUpdate").GetBoolean(),lifecycle.SourceNetUpdate);Assert.Equal(projectile.GetProperty("allFields").GetProperty("netImportant").GetBoolean(),lifecycle.NetImportant);
        }
        Assert.Equal(input.GetProperty("npcs").GetArrayLength(),npcs.ActiveCount);
        foreach(var npc in input.GetProperty("npcs").EnumerateArray())
        {Assert.True(npcs.TryGetActive(npc.GetProperty("slot").GetByte(),out var actual));Assert.Equal(npc.GetProperty("type").GetInt32(),actual.Type);Assert.Equal(npc.GetProperty("x").GetSingle(),actual.PositionX);Assert.Equal(npc.GetProperty("y").GetSingle(),actual.PositionY);}
        Assert.Empty(world.CaptureGeneratedTileEntities());
        type=0;foreach(var solid in input.GetProperty("expectedSolidity").EnumerateArray())Assert.Equal(solid.GetBoolean(),world.IsVanillaGenerationSolid(new(type++)));
        int Int(string name)=>gen.GetProperty(name).GetInt32();bool Bool(string name)=>gen.GetProperty(name).GetBoolean();
    }
    private static readonly WorldTileFlags[] Masks=[WorldTileFlags.WireRed,WorldTileFlags.WireBlue,WorldTileFlags.WireGreen,WorldTileFlags.WireYellow,WorldTileFlags.Actuator,WorldTileFlags.Inactive,WorldTileFlags.InvisibleBlock,WorldTileFlags.FullbrightBlock,WorldTileFlags.InvisibleWall,WorldTileFlags.FullbrightWall];
    private static Stream Resource(string name)
    {
        using var zip=new GZipStream(typeof(GenerationDetachedSecretMicroBiomes1458Tests).Assembly.GetManifestResourceStream("GenerationSecretMicro98."+name)??throw new InvalidOperationException("Missing explicit checkpoint fixture."),CompressionMode.Decompress);
        var memory=new MemoryStream();zip.CopyTo(memory);memory.Position=0;return memory;
    }
    private static WorldTileRegion[] Regions(JsonElement regions)=>regions.EnumerateArray().Select(r=>new WorldTileRegion(r.GetProperty("X").GetInt32(),r.GetProperty("Y").GetInt32(),r.GetProperty("Width").GetInt32(),r.GetProperty("Height").GetInt32())).ToArray();
    private static WorldChest ReadChest(JsonElement chest)=>new((short)chest.GetProperty("slot").GetInt32(),chest.GetProperty("x").GetInt32(),chest.GetProperty("y").GetInt32(),chest.GetProperty("name").GetString()!,chest.GetProperty("items").EnumerateArray().Select(i=>new WorldChestItem(i.GetProperty("stack").GetInt32(),i.GetProperty("type").GetInt32(),i.GetProperty("prefix").GetByte())).ToArray());
    private static string TileHash(Workspace world)
    {
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes))foreach(var t in world.TileStore.Tiles)
        {writer.Write((byte)(t.IsActive?1:0));writer.Write(t.Type);writer.Write(t.Wall);writer.Write(t.FrameX);writer.Write(t.FrameY);writer.Write(t.LiquidAmount);writer.Write((byte)t.LiquidKind);writer.Write((byte)(t.IsHalfBrick?1:0));writer.Write(t.SourceSlope);writer.Write(t.TileColor);writer.Write(t.WallColor);ushort flags=0;for(int bit=0;bit<10;bit++)if((t.Flags&Masks[bit])!=0)flags|=(ushort)(1<<bit);writer.Write(flags);}
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
    private sealed class SharedRandom(int seed):IWorldGenerationVanillaRandom
    {internal readonly VanillaUnifiedRandom1458 Source=new(seed);public int Next()=>Source.Next();public int Next(int max)=>Source.Next(max);public int Next(int min,int max)=>Source.Next(min,max);public double NextDouble()=>Source.NextDouble();public void NextBytes(byte[] bytes)=>Source.NextBytes(bytes);}
private sealed class SourceContext(Workspace workspace,IWorldGenerationVanillaRandom random,int seed,Action<double> progress):IWorldGenerationContext{
 public WorldGenerationRequest Request {get;}=new(Provider1458.GeneratorId,"ConfiguredSpecialMicro98",(ulong)seed,4200,1200){SeedText=seed.ToString()};
 public IWorldGenerationWorkspace Workspace=>workspace;public IWorldGenerationMetadataWorkspace Metadata=>workspace;
 public IWorldGenerationVanillaRandom VanillaRandom=>random;public IWorldGenerationRandom Random=>throw new InvalidOperationException("Micro98 requested an unowned RNG stream.");
 public CancellationToken CancellationToken=>CancellationToken.None;public void ReportProgress(double fraction,string? message=null)=>progress(fraction);
}

}
