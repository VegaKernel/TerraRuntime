using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Transient shape traversal over the candidate's existing tile/framing/structure owners.</summary>
internal sealed class MicroBiomeTraversal1458(Workspace workspace,IWorldGenerationVanillaRandom random)
{
    private readonly WorldTileStore tiles=workspace.TileStore;
    internal readonly GenerationTileFraming1458 Framing=new(workspace.TileStore,random,workspace,
        useWorldGenerationSolidity:workspace.HasVanillaGenerationSolidity);
    internal readonly GenerationWallFraming1458 Walls=new(workspace.TileStore,random);

    internal ref WorldTile At(int x,int y)
    {
        if((uint)x>=(uint)tiles.Dimensions.WidthTiles||(uint)y>=(uint)tiles.Dimensions.HeightTiles)
            throw new IndexOutOfRangeException("Micro-biome shape escaped its source tile array.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x,y)];
    }

    internal bool IsSolid(int x,int y)
    {
        var tile=At(x,y);
        return tile.IsActive&&!tile.IsActuated&&!tile.IsHalfBrick&&tile.SourceSlope==0&&
            workspace.IsVanillaGenerationSolid(tile.TileType)&&!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    internal bool IsConditionSolid(int x,int y)
    {
        if(x<10||y<10||x>=tiles.Dimensions.WidthTiles-10||y>=tiles.Dimensions.HeightTiles-10)return false;
        var tile=At(x,y);
        return tile.IsActive&&(workspace.HasVanillaGenerationSolidity?workspace.IsVanillaGenerationSolid(tile.TileType):VanillaTileCollisionCatalog.IsSolid(tile.TileType));
    }

    internal bool IsModifierSolid(int x,int y)
    {
        var tile=At(x,y);
        return tile.IsActive&&!tile.IsActuated&&(workspace.HasVanillaGenerationSolidity?workspace.IsVanillaGenerationSolid(tile.TileType):VanillaTileCollisionCatalog.IsSolid(tile.TileType))&&!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    internal void Slime(int cx,int cy,int radius,double scaleX,double scaleY,Action<int,int> action)
    {
        int squared=(radius+1)*(radius+1);
        for(int y=cy-(int)(radius*scaleY);y<=cy;y++)
        {
            double dy=(y-cy)/scaleY;
            int half=(int)Math.Min(radius*scaleX,scaleX*Math.Sqrt(squared-dy*dy));
            for(int x=cx-half;x<=cx+half;x++)action(x,y);
        }
        for(int y=cy+1;y<=cy+(int)(radius*scaleY*.5)-1;y++)
        {
            double dy=(y-cy)*(2.0/scaleY);
            int half=(int)Math.Min(radius*scaleX,scaleX*Math.Sqrt(squared-dy*dy));
            for(int x=cx-half;x<=cx+half;x++)action(x,y);
        }
    }

    internal void Runner(int originX,int originY,double strength,int steps,double velocityX,double velocityY,Action<int,int> action)
    {
        double remaining=steps,x=originX,y=originY;
        if(velocityX==0&&velocityY==0){velocityX=random.NextDouble()*2-1;velocityY=random.NextDouble()*2-1;}
        double current=strength;
        while(remaining>0&&current>0)
        {
            current=strength*(remaining/steps);remaining--;
            int left=Math.Max(1,(int)(x-current*.5)),top=Math.Max(1,(int)(y-current*.5));
            int right=Math.Min(tiles.Dimensions.WidthTiles,(int)(x+current*.5)),bottom=Math.Min(tiles.Dimensions.HeightTiles,(int)(y+current*.5));
            for(int cx=left;cx<right;cx++)for(int cy=top;cy<bottom;cy++)
                if(Math.Abs(cx-x)+Math.Abs(cy-y)<current*.5*(1.0+random.Next(-10,11)*.015))action(cx,cy);
            int advances=(int)(current/50)+1;remaining-=advances;
            x+=velocityX;y+=velocityY;
            for(int i=0;i<advances;i++)
            {
                x+=velocityX;y+=velocityY;
                velocityX+=random.NextDouble()-.5;velocityY+=random.NextDouble()-.5;
            }
            velocityX+=random.NextDouble()-.5;velocityY+=random.NextDouble()-.5;
            velocityX=Math.Clamp(velocityX,-1,1);velocityY=Math.Clamp(velocityY,-1,1);
        }
    }

    internal void Rectangle(int left,int top,int width,int height,Action<int,int> action)
    {for(int x=left;x<left+width;x++)for(int y=top;y<top+height;y++)action(x,y);}

    internal void Branch(int x,int y,double angle,double distance,List<(int X,int Y)> endpoints,Action<int,int> action)
    {
        int dx=(int)(Math.Cos(angle)*distance),dy=(int)(Math.Sin(angle)*distance);
        double length=Math.Sqrt((double)dx*dx+(double)dy*dy);int width=(int)(length/6);
        endpoints.Add((x+dx,y+dy));Segment(x,y,x+dx,y+dy,width);
        int forks=(int)(length/8);
        for(int i=0;i<forks;i++)
        {
            double ratio=(i+1)/(forks+1d);int ax=(int)(ratio*dx),ay=(int)(ratio*dy);
            double rotation=(random.NextDouble()*.5+1)*(random.Next(2)!=0?1:-1);
            double cos=Math.Cos(rotation),sin=Math.Sin(rotation);
            int bx=(int)(((dx-ax)*cos-(dy-ay)*sin)*.75)+ax;
            int by=(int)(((dx-ax)*sin+(dy-ay)*cos)*.75)+ay;
            endpoints.Add((x+bx,y+by));Segment(x+ax,y+ay,x+bx,y+by,width-1);
        }
        void Segment(int sx,int sy,int ex,int ey,int size)
        {
            size=Math.Max(1,size);
            for(int ox=-(size>>1);ox<size-(size>>1);ox++)
                for(int oy=-(size>>1);oy<size-(size>>1);oy++)PlotLine(sx+ox,sy+oy,ex,ey,action);
        }
    }

    internal static void PlotLine(int x,int y,int endX,int endY,Action<int,int> action)
    {
        if(x==endX&&y==endY){action(x,y);return;}
        bool steep=Math.Abs(endY-y)>Math.Abs(endX-x);
        if(steep){(x,y)=(y,x);(endX,endY)=(endY,endX);}
        int major=Math.Abs(endX-x),minor=Math.Abs(endY-y),error=major/2;
        int step=x<endX?1:-1,minorStep=y<endY?1:-1;
        for(int current=x;current!=endX;current+=step)
        {
            Emit(current,y);error-=minor;
            if(error<0){y+=minorStep;Emit(current,y);error+=major;}
        }
        void Emit(int majorPosition,int minorPosition)
        {if(steep)action(minorPosition,majorPosition);else action(majorPosition,minorPosition);}
    }

    internal void Root(int x,int y,double originalAngle,double distance,Action<int,int> action)
    {
        double px=x,py=y,angle=originalAngle;
        for(double progress=0;progress<distance*.85;progress++)
        {
            double ratio=progress/distance,size=4-3*ratio;
            px+=Math.Cos(angle);py+=Math.Sin(angle);
            angle+=(double)(float)random.NextDouble()-.5+(double)(float)random.NextDouble()*(originalAngle-1.5707963705062866)*.1*(1-ratio);
            angle=angle*.4+.45*Math.Clamp(angle,originalAngle-2*(1-.5*ratio),originalAngle+2*(1-.5*ratio))+
                (originalAngle+(1.5707963705062866-originalAngle)*ratio)*.15;
            for(int dx=0;dx<(int)size;dx++)for(int dy=0;dy<(int)size;dy++)action((int)px+dx,(int)py+dy);
        }
    }

    internal void Mound(int cx,int cy,int halfWidth,int height,Action<int,int> action)
    {
        double width=halfWidth;
        for(int dx=-halfWidth;dx<=halfWidth;dx++)
        {
            int depth=Math.Min(height,(int)(-(height+1)/(width*width)*(dx+width)*(dx-width)));
            for(int dy=0;dy<depth;dy++)action(cx+dx,cy-dy);
        }
    }

    internal void Circle(int cx,int cy,int horizontal,int vertical,Action<int,int> action)
    {
        int squared=(horizontal+1)*(horizontal+1);
        for(int y=cy-vertical;y<=cy+vertical;y++)
        {
            double scaled=(double)horizontal/vertical*(y-cy);
            int half=Math.Min(horizontal,(int)Math.Sqrt(squared-scaled*scaled));
            for(int x=cx-half;x<=cx+half;x++)action(x,y);
        }
    }

    internal void Blotch(int x,int y,int left,int up,int right,int down,double chance,Action<int,int> action)
    {
        random.NextDouble();
        if(random.NextDouble()>=chance){action(x,y);return;}
        int minX=random.Next(1-left,1),maxX=random.Next(0,right);
        int minY=random.Next(1-up,1),maxY=random.Next(0,down);
        for(int dx=minX;dx<=maxX;dx++)for(int dy=minY;dy<=maxY;dy++)action(x+dx,y+dy);
    }

    internal void Frame(int x,int y,bool neighbors=false)
    {
        Framing.TileFrame(x,y,true);
        if(!neighbors)return;
        Framing.TileFrame(x+1,y,true);Framing.TileFrame(x-1,y,true);
        Framing.TileFrame(x,y+1,true);Framing.TileFrame(x,y-1,true);
    }

    internal void ClearTile(int x,int y,bool neighbors=false)
    {
        ref var tile=ref At(x,y);tile.Flags&=~(WorldTileFlags.Active|WorldTileFlags.Inactive);tile.ClearSourceShape();
        if(!neighbors)return;
        Framing.TileFrame(x+1,y);Framing.TileFrame(x-1,y);Framing.TileFrame(x,y+1);Framing.TileFrame(x,y-1);
    }

    internal void SetTile(int x,int y,ushort type,bool frame=false,bool neighbors=true)
    {
        ref var tile=ref At(x,y);var flags=tile.Flags&(WorldTileFlagMasks.Wires|WorldTileFlagMasks.Actuation);
        tile=new WorldTile{Type=type,Flags=flags|WorldTileFlags.Active};if(frame)Frame(x,y,neighbors);
    }
}
