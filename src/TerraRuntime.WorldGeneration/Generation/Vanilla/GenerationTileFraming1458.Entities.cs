using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;
internal sealed partial class GenerationTileFraming1458
{
    // Entity effects share the composed canonical owners; this framer owns no entity slots.
    internal bool SourceGameMenu { get; set; } = true;
    internal bool SourceClient { get; set; }
    internal bool SourceServer { get; set; } = true;
    internal void FrameEntityObject(int i,int j,ushort type)
    {
        if(destroyingObject)return;
        if(type==231)FrameLarva(i,j);
        else if(type==444)FrameBeeHive(i,j);
        else if(type==85)FrameTombstone(i,j);
        else throw new ArgumentOutOfRangeException(nameof(type));
    }
    private void FrameLarva(int i,int j)
    {
        if(!IsFramingSolid(VanillaTileIds.Hive))return; // Explicit actual generatingWorld phase override.
        WorldTile tile=At(i,j);int col=tile.FrameX/18;
        int styleX=col>=3?col/3*54:0,left=i-col+styleX/18;
        int top=j-tile.FrameY%54/18,styleY=tile.FrameY/54*54;
        if(!Contains(left-1,top-1)||!Contains(left+3,top+3))return;
        bool broken=!HasThreeByThreeFrames(left,top,231,styleX,styleY,false);
        for(int x=left;x<left+3;x++)broken|=!SolidTileAllowBottomSlope(x,top+3);
        if(!broken)return;
        destroyingObject=true;
        for(int x=left;x<left+3;x++)for(int y=top;y<top+3;y++)
            if(At(x,y).IsActive&&At(x,y).Type==231)KillTile(x,y);
        // The cold menu context suppresses Gore and the boss call, not dust.
        if(!SourceGameMenu&&!SourceClient)
            SpawnColdObjectBoss(VanillaNpcIds.QueenBee,default);
        destroyingObject=false;
        for(int x=left-1;x<left+4;x++)for(int y=top-1;y<top+4;y++)TileFrame(x,y);
    }
    private void FrameTombstone(int i,int j)
    {
        // Source CheckSign has the caller-centred5x5 edge guard.
        if(!Contains(i-2,j-2)||!Contains(i+2,j+2))return;
        WorldTile t=At(i,j);int left=i-t.FrameX/18%2,top=j-t.FrameY/18%2;
        int styleX=At(left,top).FrameX/18/2,styleY=At(left,top).FrameY/18/2;
        bool broken=false;
        for(int x=0;x<2;x++)for(int y=0;y<2;y++){
            WorldTile m=At(left+x,top+y);
            broken|=!m.IsActive||m.Type!=85||m.FrameX/18!=styleX*2+x||m.FrameY/18!=styleY*2+y;
        }
        broken|=!SolidTileAllowBottomSlope(left,top+2)||!SolidTileAllowBottomSlope(left+1,top+2);
        if(!broken){
            // Style survives; only pixel remainders normalize.
            for(int x=0;x<2;x++)for(int y=0;y<2;y++){
                At(left+x,top+y).FrameX=(short)(styleX*36+x*18);
                At(left+x,top+y).FrameY=(short)(styleY*36+y*18);
            }
            return;
        }
        destroyingObject=true;
        for(int x=left;x<left+2;x++)for(int y=top;y<top+2;y++)if(At(x,y).Type==85)KillTile(x,y);
        // Fresh sign namespace is empty; the roll precedes the canonical Ghost count check.
        if(!SourceClient&&random.Next(GhostDenominator())==0&&(workspace?.EntityEffects??throw new NotSupportedException("Canonical NPC count owner required.")).CountNpcs(VanillaNpcIds.Ghost)<2)
            SpawnColdObjectBoss(VanillaNpcIds.Ghost,new(0,0,0,1));
        destroyingObject=false;
    }
    private int GhostDenominator(){
        if(workspace?.GenerationJourney==true)throw new NotSupportedException("Journey tombstones require their canonical per-player spawn-rate control.");
        float difficulty=workspace?.GenerationNpcDifficulty??1f;return difficulty>=3f?2:difficulty>=2f?4:8;
    }
    private void SpawnColdObjectBoss(NpcTypeId type,NpcAiState ai){
        var effects=workspace?.EntityEffects??throw new NotSupportedException("Canonical cold NPC owner required.");
        VanillaBossSpawnTile1458 Read(int x,int y){var tile=At(x,y);bool house=VanillaWallDefinitionCatalog.TryGet(tile.WallType,out var wall)&&wall.IsHousingWall;return new(tile.IsActive&&!tile.IsActuated&&IsFramingSolid(tile.TileType),house,tile.LiquidAmount>0&&tile.LiquidKind==WorldLiquidKind.Lava);}
        effects.SpawnColdBoss(type,ai,random,width,height,Read,false);
    }
}
