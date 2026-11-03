using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds.Generation;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationSpecialSeedKill1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> OriginalCases => Enumerable.Range(0, Golden.RootElement.GetProperty("cases").GetArrayLength()).Select(i => new object[] { i });

    [Theory, MemberData(nameof(OriginalCases))]
    public void Special_seed_kill_matches_original_grid_random_and_projectile_births(int index)
    {
        JsonElement row = Golden.RootElement.GetProperty("cases")[index];
        var workspace = new Workspace(80, 80);
        Assert.True(workspace.TrySetLayers(20, 50));
        workspace.InitializeVanillaGenerationSolidity();
        int identity = 0;
        foreach (JsonElement solid in Golden.RootElement.GetProperty("phase754").EnumerateArray())
            workspace.SetVanillaGenerationSolid(new TileTypeId((ushort)identity++), solid.GetBoolean());
        var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        for (int x = 0; x < 80; x++) for (int y = 0; y < 80; y++) Set(x,y,new WorldTile { FrameX = -1, FrameY = -1 });
        if(row.TryGetProperty("client",out var client))framing.SourceClient=client.GetBoolean();
        int type=row.GetProperty("type").GetInt32(), profile=row.GetProperty("profile").GetInt32(), arrangement=row.GetProperty("arrangement").GetInt32();
        VanillaSpecialWorldSeed1458 flags=profile==0?VanillaSpecialWorldSeed1458.None:VanillaSpecialWorldSeed1458.ForTheWorthy;
        if(profile is 2 or 4)flags|=VanillaSpecialWorldSeed1458.Remix;
        if(profile is 3 or 4)flags|=VanillaSpecialWorldSeed1458.DrunkWorld;
        workspace.SetVanillaSeedProfile(new(flags,VanillaSecretWorldSeed1458.None));
        var projectiles=new RuntimeProjectileStore();var npcs=new RuntimeNpcStore();
        workspace.AttachEntityEffects(new WorldGenerationEntityEffects1458(npcs,projectiles,true),0,1);
        for (int x=0;x<80;x++)for(int y=0;y<80;y++)Set(x,y,new WorldTile{FrameX=-1,FrameY=-1});
        Set(40,40,new WorldTile{Type=(ushort)type,FrameX=-1,FrameY=-1,Wall=(ushort)(type==51&&arrangement%2==0?62:64),TileColor=17,WallColor=3,LiquidAmount=(byte)(arrangement%3*91),LiquidKind=(WorldLiquidKind)(arrangement%3),Flags=WorldTileFlags.Active|WorldTileFlags.WireRed|WorldTileFlags.WireGreen|WorldTileFlags.InvisibleBlock|WorldTileFlags.FullbrightWall});
        if(type==57){int[] dx=[-1,1,0,0,-1,1,-1,1],dy=[0,0,-1,1,-1,-1,1,1];Set(40,41,new WorldTile{Type=1,FrameX=-1,FrameY=-1,Flags=WorldTileFlags.Active});if(arrangement<8)Set(40+dx[arrangement],40+dy[arrangement],new WorldTile{Type=57,FrameX=-1,FrameY=-1,Flags=WorldTileFlags.Active});else for(int x=39;x<=41;x++)Set(x,39,new WorldTile{Type=1,FrameX=-1,FrameY=-1,Flags=WorldTileFlags.Active});}
        framing.KillTile(40,40);
        JsonElement births=row.GetProperty("projectiles");Assert.Equal(births.GetArrayLength(),projectiles.ActiveCount);
        foreach(JsonElement birth in births.EnumerateArray())
        {
            Assert.True(projectiles.TryGetActive((ushort)birth.GetProperty("slot").GetInt32(),out var actual));
            Assert.True(projectiles.TryGetLifecycle(actual.Handle,out var lifecycle));
            Assert.Equal(birth.GetProperty("type").GetUInt16(),actual.Type.Value);
            Assert.Equal(birth.GetProperty("owner").GetByte(),actual.Spawner);
            Assert.Equal(birth.GetProperty("x").GetSingle(),actual.PositionX);Assert.Equal(birth.GetProperty("y").GetSingle(),actual.PositionY);
            Assert.Equal(birth.GetProperty("vx").GetSingle(),actual.VelocityX);Assert.Equal(birth.GetProperty("vy").GetSingle(),actual.VelocityY);
            Assert.Equal(birth.GetProperty("damage").GetInt16(),actual.Damage);Assert.Equal(birth.GetProperty("originalDamage").GetInt16(),actual.OriginalDamage);Assert.Equal(birth.GetProperty("knockBack").GetSingle(),actual.KnockBack);
            Assert.Equal(birth.GetProperty("timeLeft").GetInt32(),lifecycle.TimeLeft);Assert.Equal(birth.GetProperty("netUpdate").GetBoolean(),lifecycle.SourceNetUpdate);
            Assert.Equal(birth.GetProperty("ai").EnumerateArray().Select(x=>x.GetSingle()),new[]{actual.Ai.Ai0,actual.Ai.Ai1,actual.Ai.Ai2});
            Assert.Equal(10,birth.GetProperty("width").GetInt32());Assert.Equal(10,birth.GetProperty("height").GetInt32());Assert.Equal(1,birth.GetProperty("scale").GetSingle());Assert.Equal(1,birth.GetProperty("direction").GetInt32());
        }
        Assert.Equal(row.GetProperty("grid").GetString(), GridHash(workspace));
        Assert.Equal(row.GetProperty("cursor").GetInt32(), random.Cursor);
        Assert.Equal(row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()), random.Slots);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());

        void Set(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    private static string GridHash(Workspace workspace)
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

    public static IEnumerable<object[]> UnsupportedChestScopes => Enumerable.Range(0, 26).Select(i => new object[] { i });

    [Theory, MemberData(nameof(UnsupportedChestScopes))]
    public void Unsupported_special_chest_scope_refuses_before_random_or_world_changes(int index)
    {
        VanillaSpecialWorldSeed1458[] profiles = [VanillaSpecialWorldSeed1458.DrunkWorld, VanillaSpecialWorldSeed1458.ForTheWorthy,
            VanillaSpecialWorldSeed1458.CelebrationMk10, VanillaSpecialWorldSeed1458.TheConstant, VanillaSpecialWorldSeed1458.NotTheBees,
            VanillaSpecialWorldSeed1458.Remix, VanillaSpecialWorldSeed1458.NoTraps, (VanillaSpecialWorldSeed1458)255];
        var workspace = new Workspace(80,80);
        VanillaSpecialWorldSeed1458 special = index < 24 ? profiles[index / 3] : index == 24 ? VanillaSpecialWorldSeed1458.None : VanillaSpecialWorldSeed1458.ForTheWorthy | VanillaSpecialWorldSeed1458.Remix;
        var secret = index == 24 ? Enum.GetValues<VanillaSecretWorldSeed1458>().First(value => value != VanillaSecretWorldSeed1458.None) : VanillaSecretWorldSeed1458.None;
        workspace.SetVanillaSeedProfile(new(special,secret));
        var random = new RandomAdapter(1458);var slots = random.Slots;var before = GridHash(workspace);
        var context = new BuriedChestContext1458 { Height=80, WorldSurface=20, RockLayer=50, LavaLine=60, CopperBar=20, IronBar=22, SilverBar=21, GoldBar=19, TungstenIsSilverTier=false, DesertHiveLow=0, DesertHiveHigh=0, HellChestItem=[] };
        var placer = new BuriedChest1458(workspace.TileStore,random,context,workspace:workspace);
        int style = index < 24 ? index % 3 == 0 ? -1 : index % 3 == 1 ? 0 : 10 : 10;
        ushort type = (ushort)(index < 24 && index % 3 == 2 ? 467 : 21);
        Assert.Throws<NotSupportedException>(() => placer.TryAdd(40,30,out _,out _,chestStyle:style,chestTileType:type));
        Assert.Equal(0,random.Cursor);Assert.Equal(slots,random.Slots);Assert.Equal(before,GridHash(workspace));
        Assert.Empty(workspace.CaptureGeneratedChests());Assert.Equal(0,context.JungleItemCount);Assert.False(context.GeneratedShadowKey);Assert.False(context.GeneratedRamRune);
    }

    private static JsonDocument ReadGolden()
    {
        using Stream stream = typeof(GenerationSpecialSeedKill1458Tests).Assembly.GetManifestResourceStream("GenerationSpecialSeedKill1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        internal int Cursor => Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Hidden)!.GetValue(source));
        internal int[] Slots => ((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Hidden)!.GetValue(source)!).ToArray();
        public int Next() => source.Next();
        public int Next(int maximum) => source.Next(maximum);
        public int Next(int minimum, int maximum) => source.Next(minimum, maximum);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] buffer) => source.NextBytes(buffer);
    }
}
