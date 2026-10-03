using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

// Independent TerrariaServer.exe 1.4.5.8 AI/Update captures; no runtime assembly is loaded by the oracle.
public sealed class EclipseProjectile1458Tests
{
    public static IEnumerable<object[]> AiCases() => Rows("EclipseProjectileAi1458");
    public static IEnumerable<object[]> MotionCases() => Rows("EclipseProjectileMotion1458").Concat(Rows("EclipseProjectileTerrain1458"));

    [Theory, MemberData(nameof(AiCases))]
    public void Ai_matches_original_server(JsonElement row)
    {
        var shot = Shot(row.GetProperty("type").GetInt32(), F(row,"counter"), F(row,"vx"), F(row,"vy"));
        Assert.True(VanillaDefinitionCatalog.TryGet(shot.Type, out var definition));
        var context = new VanillaProjectileBehaviorContext(F(row,"wind")!=0, F(row,"wind")*.3f, .1f,
            LocalAi: new ProjectileLocalAiState(F(row,"local"),0,0));
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(in shot,in definition,in context,out var next));
        var expected=row.GetProperty("after");
        Close(F(expected,"vx"),next.VelocityX); Close(F(expected,"vy"),next.VelocityY);
        Close(expected.GetProperty("ai")[0].GetSingle(),next.Ai0);
        Close(expected.GetProperty("localAi")[0].GetSingle(),next.LocalAiOverride?.Ai0 ?? context.LocalAi.Ai0);
        Assert.Equal(expected.GetProperty("width").GetInt32(),definition.Width);
        Assert.Equal(expected.GetProperty("height").GetInt32(),definition.Height);
    }

    [Theory, MemberData(nameof(MotionCases))]
    public void Full_update_matches_original_motion_and_liquid_history(JsonElement row)
    {
        string terrain=row.GetProperty("terrain").GetString()!;
        var tiles=Terrain(terrain);
        var stepper=new VanillaProjectileWorldStateStepper(tiles);
        var shot=Shot(row.GetProperty("type").GetInt32(),F(row,"age"),F(row,"vx"),F(row,"vy"));
        var context=new ProjectileSimulationStepContext(shot,new ProjectileLifecycleState(shot.Type==VanillaProjectileIds.Nail?180:3600,false),0,1);
        Assert.True(stepper.TryStepState(in context,out var next));
        bool active=row.GetProperty("active").GetBoolean();
        float bodyOffset=!active && shot.Type==VanillaProjectileIds.DrManFlyFlask?60:0;
        Close(F(row,"afterVx"),next.State.VelocityX); Close(F(row,"afterVy"),next.State.VelocityY);
        Close(F(row,"x")+bodyOffset,next.State.PositionX); Close(F(row,"y")+bodyOffset,next.State.PositionY);
        Close(row.GetProperty("ai")[0].GetSingle(),next.State.Ai.Ai0);
        Assert.Equal(active, next.TimeLeft>0);
        if(active) Assert.Equal(row.GetProperty("timeLeft").GetInt32(),next.TimeLeft);
        Assert.Equal(row.GetProperty("wet").GetBoolean(),next.Liquid!.Value.Wet);
        Assert.Equal(row.GetProperty("honeyWet").GetBoolean(),next.Liquid.Value.HoneyWet);
        Assert.Equal(row.GetProperty("shimmerWet").GetBoolean(),next.Liquid.Value.ShimmerWet);
    }

    internal static WorldTileStore Terrain(string terrain)
    {
        var tiles=new WorldTileStore(new WorldDimensions(400,300));
        for(int x=60;x<=70;x++) for(int y=65;y<=80;y++)
        {
            WorldTile tile=default;
            if((terrain=="wall"&&x==64)||((terrain=="floor"||terrain=="halfbrick"||terrain.StartsWith("slope")||terrain.EndsWith("floor"))&&y==70))
                tile=new(){Type=1,Flags=WorldTileFlags.Active,Shape=(byte)(terrain=="halfbrick"?1:terrain.StartsWith("slope")?int.Parse(terrain[5..])+1:0)};
            if(terrain is "water" or "honey" or "shimmer" || terrain is "waterfloor" or "honeyfloor" or "shimmerfloor")
                tile=tile with{LiquidAmount=255,LiquidKind=terrain.StartsWith("water")?WorldLiquidKind.Water:terrain.StartsWith("honey")?WorldLiquidKind.Honey:WorldLiquidKind.Shimmer};
            tiles.Set(x,y,in tile);
        }
        return tiles;
    }
    internal static ProjectileSnapshot Shot(int type,float age=17,float vx=24,float vy=15.95f) => new(
        new ProjectileHandle(0,new ProjectileGeneration(1)),new ProjectileRevision(1),new ProjectileTypeId(type),255,
        1000,1100,vx,vy,new ProjectileAiState(age,0,0),0,37,2.5f,37);
    internal static ProjectileStateUpdate State(ProjectileSnapshot p)=>new(p.Type,p.Spawner,p.PositionX,p.PositionY,p.VelocityX,p.VelocityY,p.Ai,p.BannerIdToRespondTo,p.Damage,p.KnockBack,p.OriginalDamage);
    internal static void Close(float expected,float actual)=>Assert.True(MathF.Abs(expected-actual)<.0004f,$"Expected {expected:R}, actual {actual:R}");
    private static float F(JsonElement row,string name)=>row.GetProperty(name).GetSingle();
    private static IEnumerable<object[]> Rows(string name)
    {
        using var source=typeof(EclipseProjectile1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip=new GZipStream(source,CompressionMode.Decompress);
        using var document=JsonDocument.Parse(gzip);
        foreach(var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }
}
