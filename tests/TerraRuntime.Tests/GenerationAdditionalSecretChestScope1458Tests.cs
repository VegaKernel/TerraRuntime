using System.Security.Cryptography;
using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationAdditionalSecretChestScope1458Tests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Unqualified_Dungeon_call_refuses_before_initializing_shared_context_or_changing_random_or_tiles(int index)
    {
        var world=new Workspace(400,800);
        var special=index==2?VanillaSpecialWorldSeed1458.DrunkWorld:VanillaSpecialWorldSeed1458.None;
        const VanillaSecretWorldSeed1458 error=VanillaSecretWorldSeed1458.IAmError,rainbow=VanillaSecretWorldSeed1458.RainbowRoad,vampire=VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse;
        var secret=index switch{0=>error|VanillaSecretWorldSeed1458.AbandonedManors,1=>rainbow|VanillaSecretWorldSeed1458.MoreTrapsPlease,2=>vampire,3=>error|rainbow|vampire|VanillaSecretWorldSeed1458.MolePeople|VanillaSecretWorldSeed1458.MoreTrapsPlease|VanillaSecretWorldSeed1458.NegativeInfinity|VanillaSecretWorldSeed1458.Monochrome,4=>error,_=>vampire};
        world.SetVanillaSeedProfile(new(special,secret));
        var bootstrap=new VanillaWorldGenerationBootstrapState1458{HellChestItems=[],TreeX=[],TreeStyle=[],CaveBackX=[],CaveBackStyle=[],ForestBackgroundStyles=[]};
        var random=new RandomAdapter();string before=Hash();
        int style=index==4?31:index==5?0:2;ushort type=(ushort)(index==5?467:21);
        Assert.Throws<NotSupportedException>(()=>new DungeonChests1458(world,random,bootstrap,100,200,CancellationToken.None).TryPlace(200,300,type,style,155));
        Assert.Null(world.VanillaBuriedChestContext);Assert.Empty(world.CaptureGeneratedChests());Assert.Equal(before,Hash());
        Assert.Equal(new VanillaUnifiedRandom1458(1458).Next(),random.Next());Assert.Equal(1,random.Calls);
        string Hash()=>Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(world.TileStore.Tiles)));
    }
    private sealed class RandomAdapter:IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random=new(1458);internal int Calls;
        public int Next(){Calls++;return random.Next();}
        public int Next(int max){Calls++;return random.Next(max);}
        public int Next(int min,int max){Calls++;return random.Next(min,max);}
        public double NextDouble(){Calls++;return random.NextDouble();}
        public void NextBytes(byte[] bytes){Calls++;random.NextBytes(bytes);}
    }
}
