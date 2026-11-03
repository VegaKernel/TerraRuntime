using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Owns source StructureMap's ordered reservations and protected reservations for one candidate.</summary>
internal sealed class VanillaStructureMap1458(WorldTileStore tiles)
{
    private readonly List<WorldTileRegion> structures = [];
    private readonly List<WorldTileRegion> protectedStructures = [];

    internal WorldTileRegion[] CaptureStructures() => structures.ToArray();
    internal WorldTileRegion[] CaptureProtectedStructures() => protectedStructures.ToArray();

    internal void ImportCheckpoint(ReadOnlySpan<WorldTileRegion> all, ReadOnlySpan<WorldTileRegion> protectedAreas)
    {
        // Explicit detached-checkpoint input. No chest-derived or frame-important reservations.
        structures.Clear();protectedStructures.Clear();
        structures.AddRange(all.ToArray());protectedStructures.AddRange(protectedAreas.ToArray());
    }

    internal void AddStructure(WorldTileRegion region,int padding=0) => structures.Add(Inflate(region,padding));
    internal void AddProtectedStructure(WorldTileRegion region,int padding=0)
    {
        var reserved=Inflate(region,padding);structures.Add(reserved);protectedStructures.Add(reserved);
    }

    internal bool CanPlace(WorldTileRegion area,int padding=0,Func<ushort,bool>? validTile=null)
    {
        if(area.X<0||area.Y<0||area.ExclusiveRight>tiles.Dimensions.WidthTiles-1||area.ExclusiveBottom>tiles.Dimensions.HeightTiles-1)return false;
        var padded=Inflate(area,padding);
        foreach(var reserved in protectedStructures)if(Intersects(padded,reserved))return false;
        for(int x=padded.X;x<padded.ExclusiveRight;x++)for(int y=padded.Y;y<padded.ExclusiveBottom;y++)
        {
            if((uint)x>=(uint)tiles.Dimensions.WidthTiles||(uint)y>=(uint)tiles.Dimensions.HeightTiles)
                throw new IndexOutOfRangeException("Source padded structure scan escaped its tile array.");
            var tile=tiles.Get(x,y);
            if(tile.IsActive)
            {
                if(tile.Type>=VanillaWorldFormat326.TileTypeCount)
                    throw new IndexOutOfRangeException("Source structure validity table has no such tile identity.");
                if(!(validTile?.Invoke(tile.Type)??IsGeneralPlacementTile(tile.Type)))return false;
            }
        }
        return true;
    }

    internal static bool IsGeneralPlacementTile(ushort type) => type is not(
        225 or 41 or 481 or 43 or 482 or 44 or 483 or 226 or 203 or 112 or 25 or 70 or 151 or 21 or 31 or
        696 or 467 or 12 or 665 or 639 or 138 or 664 or 711 or 712 or 713 or 714 or 715 or 716);

    private static WorldTileRegion Inflate(WorldTileRegion r,int padding)=>new(r.X-padding,r.Y-padding,r.Width+padding*2,r.Height+padding*2);
    private static bool Intersects(WorldTileRegion a,WorldTileRegion b)=>a.X<b.ExclusiveRight&&b.X<a.ExclusiveRight&&a.Y<b.ExclusiveBottom&&b.Y<a.ExclusiveBottom;
}
