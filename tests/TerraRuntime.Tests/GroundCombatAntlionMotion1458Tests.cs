using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCombatAntlionMotion1458Tests
{
    [Fact]
    public void Three_antlion_profiles_match_independent_original_full_npc_motion_and_random_state()
        => RunFixture("GroundCombatAntlionMotionLinux1458",18);

    [Fact]
    public void Source_busy_actions_skip_day_and_stuck_idle_work_until_the_action_expires()
        => RunFixture("GroundCombatBusyActionLinux1458",15);

    [Fact]
    public void Antlion_terrain_probe_uses_source_body_half_plus_sixteen_reach()
        => RunFixture("GroundCombatAntlionProbeLinux1458",6);

    internal static void RunFixture(string resourceName,int expectedCount)
    {
        using var resource=typeof(GroundCombatAntlionMotion1458Tests).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(resource);
        using var gzip=new GZipStream(resource,CompressionMode.Decompress);
        using var document=JsonDocument.Parse(gzip);
        int cases=0;
        var shotBuffer=new ProjectileSnapshot[8];
        foreach(var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            int type=row.GetProperty("type").GetInt32(), floor=row.GetProperty("floor").GetInt32();
            var before=row.GetProperty("before"); var expected=row.GetProperty("after");
            string scene=row.GetProperty("scene").GetString()!;
            int mode=resourceName is "GroundCombatAntlionMotionLinux1458" or "GroundCombatBusyActionLinux1458" or "GroundCombatAntlionProbeLinux1458" ? 0 : row.GetProperty("mode").GetInt32();
            var tiles=new WorldTileStore(new(192,200));Assert.True(tiles.TryAttachWorldSurface(80d));
            for(int x=0;x<192;x++)tiles.Set(x,floor,new WorldTile{Type=1,Flags=WorldTileFlags.Active});
            if(scene.StartsWith("stuck",StringComparison.Ordinal))
                for(int y=floor-4;y<floor;y++)tiles.Set(66,y,new WorldTile{Type=1,Flags=WorldTileFlags.Active});
            int caseSeed=row.TryGetProperty("caseSeed",out var seedValue)?seedValue.GetInt32():1458;
            var npcs=new RuntimeNpcStore();var shots=new RuntimeProjectileStore();var random=new VanillaUnifiedRandom1458(caseSeed);
            var world=new ServerRuntimeState(npcs:npcs,projectiles:shots,worldTiles:tiles,expertMode:mode==1,
                worldClock:new RuntimeWorldClock(0,scene.StartsWith("day",StringComparison.Ordinal),(VanillaMoonPhase)0,0,0),
                townCommerceWorldFacts:RuntimeTownCommerceWorldFacts1458.FromMetadata(new WorldFileRuntimeMetadata{GameMode=(byte)mode}),
                naturalSpawnRandom:new SystemVanillaNpcRandom(random));
            var slots=new PlayerSlotPool(1);Assert.True(slots.TryAcquireConnection(out var lease));
            using var session=new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest();session.ObserveSectionRequest();
            var connection=new ConnectionHandle(GameCommandSourceId.FromConnection(9182),session.Handle);
            world.Apply(new PlayerSpawnRuntimeCommand(connection,session,new(session.Slot,40,(short)floor,0,0,0,0,0)));
            world.Apply(new PlayerMovementRuntimeCommand(connection,new(session.Slot,0,0,0,0,0,
                row.GetProperty("playerX").GetSingle(),floor*16f-42f,false,0,0,false,0,false,0,0,0,0,false,0,0)));
            world.Apply(new PlayerStealthRuntimeCommand(connection,1f));
            world.Apply(new PlayerZonesRuntimeCommand(connection,default));
            var simulation=NpcSimulationState.Initial with {
                DirectionX=I(before,"direction"),DirectionY=I(before,"directionY"),SpriteDirection=I(before,"spriteDirection"),
                OldPositionX=F(before.GetProperty("oldPosition")[0]),OldPositionY=F(before.GetProperty("oldPosition")[1]),
                OldVelocityX=F(before.GetProperty("oldVelocity")[0]),OldVelocityY=F(before.GetProperty("oldVelocity")[1]),
                Life=I(before,"life"),LifeMax=I(before,"lifeMax"),BaseLifeMax=I(before,"baseLifeMax"),
                HomeTileX=I(before,"homeX"),HomeTileY=I(before,"homeY"),Homeless=before.GetProperty("homeless").GetBoolean(),
                FrameIndex=I(before,"frameIndex"),FrameCounter=D(before.GetProperty("frameCounter")),
                LocalAi=A(before.GetProperty("localAi")),SpawnDifficulty=mode==1?2f:1f,
                DontTakeDamage=before.GetProperty("dontTakeDamage").GetBoolean(),JustHit=before.GetProperty("justHit").GetBoolean()
            };
            var creation=new NpcStateUpdate(type,(short)type,F(before.GetProperty("position")[0]),F(before.GetProperty("position")[1]),
                F(before.GetProperty("velocity")[0]),F(before.GetProperty("velocity")[1]),(ushort)I(before,"target"),A(before.GetProperty("ai")),simulation);
            Assert.True(npcs.TrySpawnVanilla(in creation,out var npc));
            // Reapply only these observed imported motion facts after real source-default materialization.
            var update=creation with {Simulation=npc.Simulation with {
                DirectionX=simulation.DirectionX,DirectionY=simulation.DirectionY,SpriteDirection=simulation.SpriteDirection,
                OldPositionX=simulation.OldPositionX,OldPositionY=simulation.OldPositionY,
                OldVelocityX=simulation.OldVelocityX,OldVelocityY=simulation.OldVelocityY,
                HomeTileX=simulation.HomeTileX,HomeTileY=simulation.HomeTileY,Homeless=simulation.Homeless,
                FrameIndex=simulation.FrameIndex,FrameCounter=simulation.FrameCounter,LocalAi=simulation.LocalAi,
                DontTakeDamage=simulation.DontTakeDamage,JustHit=simulation.JustHit }};
            Assert.True(npcs.TryUpdate(npc.Handle,in update,out npc));
            AssertRng(row.GetProperty("rngBefore"),random);
            NonresidentBunnyNpcPhase1458.Tick(world);
            Assert.True(npcs.TryGet(npc.Handle,out var after));
            string label=$"{type}/{row.GetProperty("scene").GetString()}";
            Assert.Equal(Bits(expected.GetProperty("position")[0]),BitConverter.SingleToUInt32Bits(after.PositionX));
            Assert.Equal(Bits(expected.GetProperty("position")[1]),BitConverter.SingleToUInt32Bits(after.PositionY));
            Assert.True(Bits(expected.GetProperty("velocity")[0])==BitConverter.SingleToUInt32Bits(after.VelocityX),label);
            Assert.True(Bits(expected.GetProperty("velocity")[1])==BitConverter.SingleToUInt32Bits(after.VelocityY),label);
            Assert.Equal(A(expected.GetProperty("ai")),after.Ai);
            Assert.Equal(A(expected.GetProperty("localAi")),after.Simulation.LocalAi);
            Assert.Equal((I(expected,"direction"),I(expected,"directionY"),I(expected,"spriteDirection")),
                (after.Simulation.DirectionX,after.Simulation.DirectionY,after.Simulation.SpriteDirection));
            Assert.Equal((ushort)I(expected,"target"),after.Target);
            Assert.Equal(I(expected,"timeLeft"),after.Simulation.TimeLeft);
            Assert.Equal(expected.GetProperty("dontTakeDamage").GetBoolean(),after.Simulation.DontTakeDamage);
            Assert.Equal(I(expected,"frameIndex"),after.Simulation.FrameIndex);
            Assert.Equal(D(expected.GetProperty("frameCounter")),after.Simulation.FrameCounter);
            AssertRng(row.GetProperty("rngAfter"),random);
            if(row.TryGetProperty("afterProjectiles",out var expectedShots))
            {
                int count=shots.CopyActive(shotBuffer);Assert.Equal(expectedShots.GetArrayLength(),count);
                for(int i=0;i<count;i++)AssertShot(expectedShots[i],shotBuffer[i],shots,after.Handle);
            }
            cases++;
        }
        Assert.Equal(expectedCount,cases);
    }

    private static void AssertShot(JsonElement expected,in ProjectileSnapshot actual,RuntimeProjectileStore store,NpcHandle source)
    {
        Assert.Equal(I(expected,"slot"),(int)actual.Handle.Slot);
        Assert.Equal(I(expected,"type"),actual.Type.Value);Assert.Equal(I(expected,"owner"),(int)actual.Spawner);
        Assert.Equal(Bits(expected.GetProperty("position")[0]),BitConverter.SingleToUInt32Bits(actual.PositionX));
        Assert.Equal(Bits(expected.GetProperty("position")[1]),BitConverter.SingleToUInt32Bits(actual.PositionY));
        Assert.Equal(Bits(expected.GetProperty("velocity")[0]),BitConverter.SingleToUInt32Bits(actual.VelocityX));
        Assert.Equal(Bits(expected.GetProperty("velocity")[1]),BitConverter.SingleToUInt32Bits(actual.VelocityY));
        Assert.Equal(new[]{Bits(expected.GetProperty("ai")[0]),Bits(expected.GetProperty("ai")[1]),Bits(expected.GetProperty("ai")[2])},
            new[]{BitConverter.SingleToUInt32Bits(actual.Ai.Ai0),BitConverter.SingleToUInt32Bits(actual.Ai.Ai1),BitConverter.SingleToUInt32Bits(actual.Ai.Ai2)});
        Assert.Equal(I(expected,"damage"),(int)actual.Damage);Assert.Equal(I(expected,"originalDamage"),(int)actual.OriginalDamage);
        Assert.Equal(Bits(expected.GetProperty("knockBack")),BitConverter.SingleToUInt32Bits(actual.KnockBack));
        Assert.True(store.TryGetLifecycle(actual.Handle,out var life));Assert.Equal(I(expected,"timeLeft"),life.TimeLeft);
        Assert.Equal(expected.GetProperty("npcProj").GetBoolean(),life.NpcProjectile);
        Assert.Equal(new[]{Bits(expected.GetProperty("localAi")[0]),Bits(expected.GetProperty("localAi")[1]),Bits(expected.GetProperty("localAi")[2])},
            new[]{BitConverter.SingleToUInt32Bits(life.LocalAi.Ai0),BitConverter.SingleToUInt32Bits(life.LocalAi.Ai1),BitConverter.SingleToUInt32Bits(life.LocalAi.Ai2)});
        Assert.True(VanillaDefinitionCatalog.TryGet(actual.Type,out var shape));
        Assert.Equal((I(expected,"width"),I(expected,"height"),expected.GetProperty("tileCollide").GetBoolean(),expected.GetProperty("ignoreWater").GetBoolean()),
            (shape.Width,shape.Height,shape.TileCollide,shape.IgnoreWater));
        Assert.True(RuntimeProjectilePacketProjection.TryCreateCanonicalKey(in actual,out var key));var expectedKey=expected.GetProperty("key");
        Assert.Equal((I(expectedKey,"spawner"),I(expectedKey,"index"),I(expectedKey,"generation")),((int)key.Spawner,(int)key.ProjectileIndex,(int)key.Generation));
        Assert.True(store.TryGetServerNpcSource(actual.Handle,out var actualSource));Assert.Equal(source,actualSource);
    }

    private static uint Bits(JsonElement x)=>uint.Parse(x.GetString()!,NumberStyles.HexNumber,CultureInfo.InvariantCulture);
    private static float F(JsonElement x)=>BitConverter.UInt32BitsToSingle(Bits(x));
    private static double D(JsonElement x)=>BitConverter.UInt64BitsToDouble(ulong.Parse(x.GetString()!,NumberStyles.HexNumber,CultureInfo.InvariantCulture));
    private static int I(JsonElement x,string field)=>x.GetProperty(field).GetInt32();
    private static NpcAiState A(JsonElement x)=>new(F(x[0]),F(x[1]),F(x[2]),F(x[3]));
    private static void AssertRng(JsonElement expected,VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),(uint)typeof(VanillaUnifiedRandom1458).GetField("inext",flags)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",flags)!.GetValue(random)!);
    }
}
