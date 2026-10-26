using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private const ushort LarvaTile = 231;
        private bool larvaDestroying;
        private int larvaLeft,larvaTop,larvaOriginX,larvaOriginY;

        private bool TryVerifyLarvaFootprint(int x,int y,in WorldTile tile,out int left,out int top)
        {
            left=top=0;
            if(tile.Type!=LarvaTile || tile.FrameX<0 || tile.FrameY<0 || tile.FrameX%18!=0 || tile.FrameY%18!=0)return false;
            left=x-tile.FrameX/18%3;top=y-tile.FrameY%54/18;
            int frameX=tile.FrameX/54*54,frameY=tile.FrameY/54*54;
            for(int dx=0;dx<3;dx++)for(int dy=0;dy<3;dy++)
            {
                // Admission proves a real intact source object before our own cell clears.
                // Malformed imported fragments remain outside this qualified producer.
                if(!Overlay.TryReadBefore(left+dx,top+dy,out var before) || !before.IsActive ||
                    before.Type!=LarvaTile || before.FrameX!=frameX+dx*18 || before.FrameY!=frameY+dy*18)return false;
            }
            return true;
        }
        private bool FrameLarva(int x,int y,in WorldTile tile)
        {
            if(IsSourceDestroyingObject)return true;
            if(!TryVerifyLarvaFootprint(x,y,in tile,out int left,out int top))return false;
            int frameX=tile.FrameX/54*54,frameY=tile.FrameY/54*54;
            bool intact=true;
            for(int dx=0;dx<3;dx++)for(int dy=0;dy<3;dy++)
            {
                if(!Overlay.TryReadSourceSafely(left+dx,top+dy,out var cell))return false;
                intact &= cell.IsActive && cell.Type==LarvaTile && cell.FrameX==frameX+dx*18 && cell.FrameY==frameY+dy*18;
            }
            bool supported=true;
            for(int dx=0;dx<3;dx++)
            {
                if(!Overlay.TryReadSourceSafely(left+dx,top+3,out var support) ||
                    !VanillaToolTileCatalog1458.TryGet(support.TileType,out var definition))return false;
                int frame=support.FrameX/18;
                bool properPlatform=definition.IsPlatform && (frame is >=0 and <=7 or >=12 and <=16 or >=25 and <=26);
                supported &= support.IsActive && (definition.IsSolid || definition.IsSolidTop) &&
                    !support.IsHalfBrick && !support.IsActuated && (support.SourceSlope is not (1 or 2) || properPlatform);
            }
            if(intact && supported)return true;
            larvaDestroying=true;larvaLeft=left;larvaTop=top;larvaOriginX=x;larvaOriginY=y;
            return Push(new(StepKind.LarvaNextCell,left,top){Index=0});
        }
        private bool LarvaNextCell(int index)
        {
            if(index==9)return Push(new(StepKind.LarvaAfterKills,larvaOriginX,larvaOriginY));
            int x=larvaLeft+index/3,y=larvaTop+index%3;
            if(!Overlay.TryReadSourceSafely(x,y,out var tile) ||
                !Push(new(StepKind.LarvaNextCell,larvaLeft,larvaTop){Index=index+1}))return false;
            return !tile.IsActive || tile.Type!=LarvaTile || Push(new(StepKind.Kill,x,y));
        }
        private bool LarvaAfterKills()
        {
            // Dedicated-server NewGore returns before RNG. The local presentation
            // projection uses the same available-gore profile as the independent caller capture.
            if(clientRole)for(int gore=0;gore<3;gore++){random.Next(10,31);random.Next(-20,21);}
            if(!clientRole && (server is null || !server.TryStageLarvaBoss(larvaOriginX,larvaOriginY,actors)))return false;
            larvaDestroying=false;
            for(int dx=3;dx>=-1;dx--)for(int dy=3;dy>=-1;dy--)
                if(!Push(new(StepKind.Frame,larvaLeft+dx,larvaTop+dy)))return false;
            return true;
        }
    }
}
