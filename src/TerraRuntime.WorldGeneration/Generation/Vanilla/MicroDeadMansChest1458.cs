using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed class MicroDeadMansChest1458(Workspace workspace,IWorldGenerationVanillaRandom random,double oceanLevel,int beachDistance=380,int maximumTileCount=3500)
{
    private readonly MicroBiomeTraversal1458 shape=new(workspace,random);
    private readonly List<(int X,int Y,int Direction,int Push)> darts=[];
    private readonly List<(int X,int Y,int Push,int Material)> boulders=[];
    private readonly List<(int X,int Y,int Dx,int Dy,int Steps)> wires=[];
    private readonly List<(int X,int Y)> explosives=[];

    internal List<(int X,int Y)> GetPossibleChests()
    {
        var result=new List<(int X,int Y)>();
        foreach(var chest in workspace.CaptureGeneratedChests())
        {
            if(!IsGood(chest.X,chest.Y))continue;
            Clear();FindBoulders(chest.X,chest.Y+1);FindDarts(chest.X,chest.Y+1);
            if(Enough()&&workspace.VanillaStructureMap.CanPlace(new(chest.X,chest.Y,1,1),10,IsAllowed))result.Add((chest.X,chest.Y));
        }
        return result;
    }

    internal bool Place(int x,int y)
    {
        if(!IsGood(x,y))return false;
        Clear();FindBoulders(x,y+1);FindDarts(x,y+1);FindExplosives(x,y+1);
        if(!Enough())return false;
        for(int dx=0;dx<2;dx++)for(int dy=0;dy<2;dy++)
        {ref var tile=ref shape.At(x+dx,y+dy);tile.Type=467;tile.FrameX=(short)(144+dx*18);tile.FrameY=(short)(dy*18);}
        if(random.Next(3)==0)
        {
            var chest=workspace.CaptureGeneratedChests().FirstOrDefault(c=>c.X==x&&c.Y==y);
            if(chest is not null)
            {
                var items=(WorldChestItem[])chest.Items.Clone();
                for(int i=items.Length-2;i>0;i--)if(items[i].Stack!=0)items[i+1]=items[i];
                items[1]=new(1,5007,0);
                var replacement=items.Select(i=>new WorldGenerationChestItem(i.Stack,new(i.ItemType),new(i.Prefix))).ToArray();
                if(!workspace.TryReplaceGeneratedChestItems(x,y,replacement))throw new InvalidOperationException("The trap chest lost its canonical chest record.");
            }
        }
        foreach(var dart in darts)
        {
            ref var tile=ref shape.At(dart.X+dart.Direction*dart.Push,dart.Y);tile.Type=137;tile.FrameX=(short)(dart.Direction==-1?18:0);tile.FrameY=0;tile.ClearSourceShape();
            shape.Framing.TileFrame(dart.X,dart.Y,true);Wire(dart.X,dart.Y,dart.Direction,0,dart.Push);
        }
        foreach(var wire in wires)Wire(wire.X,wire.Y,wire.Dx,wire.Dy,wire.Steps);
        foreach(var boulder in boulders)PlaceBoulder(boulder.X,boulder.Y,boulder.Push,boulder.Material);
        foreach(var point in explosives)
        {ref var tile=ref shape.At(point.X,point.Y);tile.Type=141;tile.FrameX=tile.FrameY=0;tile.ClearSourceShape();shape.Framing.TileFrame(point.X,point.Y,true);}
        if(explosives.Count>0)
        {
            Wire(x,y,0,1,explosives[0].Y-y);
            Wire(explosives.Min(p=>p.X),explosives[0].Y,1,0,explosives.Max(p=>p.X)-explosives.Min(p=>p.X));
        }
        return true;
    }

    private bool IsGood(int x,int y)
    {
        if(x<50||y<50||x>=workspace.WidthTiles-50||y>=workspace.HeightTiles-50||y<=oceanLevel&&(x<beachDistance||x>workspace.WidthTiles-beachDistance))return false;
        var tile=shape.At(x,y);if(tile.Type!=21||tile.FrameX/36!=1||!CanClear(shape.At(x,y+2)))return false;
        for(int dx=-20;dx<=20;dx++)for(int dy=-20;dy<=20;dy++)if((shape.At(x+dx,y+dy).Flags&WorldTileFlagMasks.Wires)!=0)return false;
        return new GenerationTileCount1458(workspace.TileStore,workspace.IsVanillaGenerationSolid){MaxTileCount=maximumTileCount}.Count(x,y,false,true)>=40;
    }
    private static bool IsAllowed(ushort type)=>type is 21 or 467 or 138 or 664 or 712 or 713 or 714 or 715||VanillaStructureMap1458.IsGeneralPlacementTile(type);
    private static bool Container(in WorldTile tile)=>tile.Type is 21 or 467 or 88;
    private static bool CanClear(in WorldTile tile)=>WorldSmoothingCatalog1458.CanBeClearedDuringGeneration(tile.TileType);
    private bool PhaseSolid(in WorldTile tile)=>workspace.IsVanillaGenerationSolid(tile.TileType);
    private bool Enough()=>(boulders.Count>0||explosives.Count>0)&&darts.Count>0;
    private void Clear(){darts.Clear();boulders.Clear();wires.Clear();explosives.Clear();}
    private void Wire(int x,int y,int dx,int dy,int steps)
    {for(int step=0;step<=steps;step++)shape.At(x+dx*step,y+dy*step).Flags|=WorldTileFlags.WireRed;}

    private void FindBoulders(int x,int y)
    {
        int count=random.Next(2,5),spacing=random.Next(2,5),column=x-count/2*spacing,baseline=y-6;
        for(int i=0;i<=count;i++,column+=spacing)
            for(int step=0;step<50;step++)if(shape.At(column,baseline-step).IsActive){FindBoulder(column,baseline-step,step);break;}
        if(boulders.Count==0)return;
        int min=Math.Min(x,boulders.Min(p=>p.X)),max=Math.Max(x,boulders.Max(p=>p.X));
        wires.Add((min,baseline-1,1,0,max-min));wires.Add((x,y,0,-1,7));
    }
    private void FindBoulder(int x,int y,int push)
    {
        Span<int> counts=stackalloc int[754];
        for(int cx=x;cx<x+2;cx++)for(int cy=y-4;cy<=y;cy++)
        {
            var tile=shape.At(cx,cy);
            if(tile.IsActive&&!VanillaWorldFrameImportance326.IsFrameImportant(tile.Type)&&PhaseSolid(tile))counts[tile.Type]++;
            if(tile.IsActive&&(!CanClear(tile)||Container(tile)))return;
        }
        for(int cx=x-1;cx<x+3;cx++)for(int cy=y-5;cy<=y-2;cy++)
        {var tile=shape.At(cx,cy);if(!tile.IsActive||Container(tile))return;}
        if(shape.At(x,y+1).IsActive&&shape.At(x+1,y+1).IsActive)return;
        for(int cx=x-2;cx<=x+3;cx++)for(int cy=y-6;cy<=y-1;cy++)
        {var tile=shape.At(cx,cy);if(tile.IsActive&&(Container(tile)||tile.Type is 12 or 665 or 639))return;}
        int material=0;for(int i=1;i<counts.Length;i++)if(counts[i]>counts[material])material=i;
        boulders.Add((x,y,push-1,material));
    }
    private void FindDarts(int x,int y)
    {
        int count=random.Next(3,7),direction=random.Next(2)!=0?1:-1,last=-1;
        for(int i=0;i<count;i++,direction=-direction,y--)
            if(FindDart(x,y,direction))last=i;
        wires.Add((x,y+count,0,-1,last));
    }
    private bool FindDart(int x,int y,int direction)
    {
        for(int step=0;step<20;step++)
        {
            var tile=shape.At(x+step*direction,y);if(!tile.IsActive||Container(tile)||!PhaseSolid(tile))continue;
            if(step<5||tile.HasActuator||VanillaWorldFrameImportance326.IsFrameImportant(tile.Type)||!CanClear(tile))return false;
            darts.Add((x,y,direction,step));return true;
        }
        return false;
    }
    private void FindExplosives(int x,int y)
    {
        y+=3;var eligible=new List<int>();
        for(int i=x;i<=x+1;i++)if(ExplosiveFloor(i,y))eligible.Add(i);
        int center=eligible.Count==0?-1:eligible[random.Next(eligible.Count)];eligible.Clear();
        int rightStart=x+1+random.Next(2,6);
        for(int i=rightStart;i<rightStart+4;i++)if(ExplosiveFloor(i,y))eligible.Add(i);
        int right=eligible.Count==0?-1:eligible[random.Next(eligible.Count)];
        int leftStart=x-4-random.Next(2,6);
        for(int i=leftStart;i<leftStart+4;i++)if(ExplosiveFloor(i,y))eligible.Add(i);
        int left=eligible.Count==0?-1:eligible[random.Next(eligible.Count)];
        if(left!=-1)explosives.Add((left,y));if(center!=-1)explosives.Add((center,y));if(right!=-1)explosives.Add((right,y));
    }
    private bool ExplosiveFloor(int x,int y)
    {var tile=shape.At(x,y);return tile.IsActive&&!Container(tile)&&PhaseSolid(tile)&&!VanillaWorldFrameImportance326.IsFrameImportant(tile.Type)&&!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);}
    private void PlaceBoulder(int x,int y,int push,int material)
    {
        for(int cx=x;cx<x+2;cx++)for(int cy=y-4;cy<=y+2;cy++)
        {
            if(cy<y-2||cy>y){shape.ClearTile(cx,cy);continue;}
            for(int attempt=0;attempt<2;attempt++)
            {
                ref var tile=ref shape.At(cx,cy);if(!tile.IsActive){tile.Flags|=WorldTileFlags.Active;tile.Type=(ushort)material;}
                tile.ClearSourceShape();shape.Framing.TileFrame(cx,cy,true);if(tile.IsActive)break;
            }
            ref var support=ref shape.At(cx,cy);support.Flags|=WorldTileFlags.WireRed;if(PhaseSolid(support))support.Flags|=WorldTileFlags.Actuator;
        }
        int anchorX=x+1,anchorY=y-3;
        for(int cx=anchorX-3;cx<=anchorX+2;cx++)for(int cy=anchorY-3;cy<=anchorY+2;cy++)
        {ref var tile=ref shape.At(cx,cy);if(GenerationObjectSupport1458.IsBoulder(tile.Type))continue;tile.Type=1;if((tile.Flags&WorldTileFlags.WireRed)!=0)tile.Flags|=WorldTileFlags.Actuator;}
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,anchorX,anchorY,138);
        Wire(x,y,0,1,push);
    }
}
