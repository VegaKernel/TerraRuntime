using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public sealed partial class WorldTileStore
{
    private bool canonicalCobwebFramesZero;
    private Dictionary<int, CobwebFrameClaim>? cobwebFrames;
    private Dictionary<int, ulong>? unknownColdFramePages;
    private readonly record struct CobwebFrameClaim(WorldTile Tile, byte? Number);

    // Source clearWorld -> LoadWorldTiles leaves frameNumber=0; frame coordinates do not encode this field.
    // Startup caches, imported stores and generation candidates do not establish this canonical-load provenance.
    internal void MarkCanonicalCobwebFrameNumbersZero() => canonicalCobwebFramesZero = true;

    private void InvalidateCobwebFrameNumber(int index, in WorldTile tile)
    {
        if (canonicalCobwebFramesZero)
        {
            var pages = unknownColdFramePages ??= [];
            int page = index >> 6;
            pages.TryGetValue(page, out ulong bits);
            pages[page] = bits | (1UL << (index & 63));
        }
        if (cobwebFrames?.ContainsKey(index) == true)
            (cobwebFrames ??= [])[index] = new(tile, null);
    }

    public bool TryGetCobwebFrameNumber(int x, int y, out byte number)
    {
        number = 0;
        int index = GetIndex(x, y); WorldTile current = _tiles[index];
        if (!current.IsActive || current.Type != VanillaTileIds.Cobweb.Value) return false;
        return TryGetOwnedCobwebBank(index, in current, out number);
    }

    // PlaceTile preserves the old inactive tile's bank; an early recursive cosmetic visit can read it.
    // This is scoped to the destination of a planned cobweb, not a general active-tile framing owner.
    public bool TryGetCobwebPlacementFrameNumber(int x, int y, out byte number)
    {
        number = 0; int index = GetIndex(x, y); WorldTile current = _tiles[index];
        return !current.IsActive && TryGetOwnedCobwebBank(index, in current, out number);
    }

    private bool TryGetOwnedCobwebBank(int index, in WorldTile current, out byte number)
    {
        number = 0;
        if (cobwebFrames?.TryGetValue(index, out var claim) == true)
        {
            WorldTile expected = claim.Tile;
            if (claim.Number is not byte owned || !FrameTileEquals(in current, in expected)) return false;
            number = owned; return true;
        }
        return canonicalCobwebFramesZero &&
            !(unknownColdFramePages?.TryGetValue(index >> 6, out ulong bits) == true &&
              (bits & (1UL << (index & 63))) != 0);
    }

    public bool TryRetainCobwebFrameNumber(int x, int y, in WorldTile expected, byte number)
    {
        if (number > 2 || !expected.IsActive || expected.Type != VanillaTileIds.Cobweb.Value) return false;
        int index = GetIndex(x, y); WorldTile current = _tiles[index];
        if (!FrameTileEquals(in current, in expected)) return false;
        (cobwebFrames ??= [])[index] = new(expected, number); return true;
    }

    public bool TryRetainCobwebPlacementFrameNumber(int x, int y, in WorldTile expected, byte number)
    {
        if (number > 2 || expected.IsActive) return false;
        int index = GetIndex(x, y); WorldTile current = _tiles[index];
        if (!FrameTileEquals(in current, in expected)) return false;
        (cobwebFrames ??= [])[index] = new(expected, number); return true;
    }

    private static bool FrameTileEquals(in WorldTile left, in WorldTile right) =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in left, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in right, 1)));
}
