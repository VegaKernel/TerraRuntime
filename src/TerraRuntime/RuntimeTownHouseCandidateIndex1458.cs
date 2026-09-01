using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct RuntimeTownHouseCandidate1458(
    int SeedTileX,
    int SeedTileY,
    int HomeTileX,
    int HomeTileY);

/// <summary>
/// Bounded authoritative house-discovery index. Scanning is intentionally cheap: only furniture identities that
/// participate in the pinned RoomNeeds sets invoke the full source-shaped housing validator. Every candidate is
/// revalidated against the requested NPC type and live occupants before use, so stale tile edits fail closed.
/// Candidate order is stable discovery order and can be consumed by the room-aware SpawnTownNPC selector.
/// </summary>
internal sealed class RuntimeTownHouseCandidateIndex1458
{
    private const int DefaultMaximumCandidates = 1024;

    private readonly WorldTileStore tiles;
    private readonly VanillaHousingValidator1458 validator;
    private readonly List<RuntimeTownHouseCandidate1458> candidates = [];
    private readonly HashSet<long> canonicalHomes = [];
    private readonly int maximumCandidates;
    private long scanCursor;

    public RuntimeTownHouseCandidateIndex1458(
        WorldTileStore tiles,
        VanillaHousingValidator1458 validator,
        int maximumCandidates = DefaultMaximumCandidates)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);
        this.tiles = tiles;
        this.validator = validator;
        this.maximumCandidates = maximumCandidates;
    }

    public int CandidateCount => candidates.Count;

    public void SetTruffleUnlocked(bool unlocked) => validator.SetTruffleUnlocked(unlocked);

    internal VanillaHousingValidator1458 Validator => validator;

    public void Scan(int tileBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tileBudget);
        if (tileBudget == 0 || candidates.Count >= maximumCandidates)
            return;

        WorldDimensions dimensions = tiles.Dimensions;
        long totalTiles = checked((long)dimensions.WidthTiles * dimensions.HeightTiles);
        for (int scanned = 0; scanned < tileBudget; scanned++)
        {
            long linear = scanCursor++;
            if (scanCursor >= totalTiles)
                scanCursor = 0;
            if (linear >= totalTiles)
                linear %= totalTiles;

            int x = checked((int)(linear / dimensions.HeightTiles));
            int y = checked((int)(linear % dimensions.HeightTiles));
            WorldTile tile = tiles.Get(x, y);
            if (!tile.IsActive || tile.IsActuated || !VanillaHousingValidator1458.IsPotentialRoomAnchorType(tile.Type))
                continue;

            VanillaHousingPlacement placement = validator.Validate(x, y, VanillaNpcIds.Merchant);
            if (!placement.IsValid)
                continue;

            long key = Pack(placement.HomeTileX, placement.HomeTileY);
            if (!canonicalHomes.Add(key))
                continue;

            candidates.Add(new RuntimeTownHouseCandidate1458(
                x,
                y,
                placement.HomeTileX,
                placement.HomeTileY));
            if (candidates.Count >= maximumCandidates)
                break;
        }
    }

    public RuntimeTownHouseCandidate1458[] CaptureCandidates() => candidates.ToArray();

    /// <summary>
    /// Terraria 1.4.5.8 SpawnTownNPC gives a TownManager-assigned room one recursive attempt before the room that
    /// happened to trigger the current housing scan. TownManager stores the canonical home floor tile; WorldGen
    /// restarts its room check two tiles above that point.
    /// </summary>
    public bool TryValidateAssignedRoom(
        NpcTypeId npcType,
        in WorldTownRoom room,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out VanillaHousingPlacement placement)
    {
        int seedY = room.Y - 2;
        if ((uint)room.X >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)seedY >= (uint)tiles.Dimensions.HeightTiles)
        {
            placement = default;
            return false;
        }

        placement = validator.Validate(room.X, seedY, npcType, occupants);
        return placement.IsValid && placement.HomeTileX == room.X && placement.HomeTileY == room.Y;
    }

    public bool TryValidateCandidate(
        in RuntimeTownHouseCandidate1458 candidate,
        NpcTypeId npcType,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out VanillaHousingPlacement placement)
    {
        placement = validator.Validate(candidate.SeedTileX, candidate.SeedTileY, npcType, occupants);
        return placement.IsValid;
    }

    public bool TryFindRoom(
        NpcTypeId npcType,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out VanillaHousingPlacement placement) =>
        TryFindRoom(npcType, occupants, out _, out placement);

    public bool TryFindRoom(
        NpcTypeId npcType,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
        out RuntimeTownHouseCandidate1458 selectedCandidate,
        out VanillaHousingPlacement placement)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            RuntimeTownHouseCandidate1458 candidate = candidates[i];
            VanillaHousingPlacement current = validator.Validate(
                candidate.SeedTileX,
                candidate.SeedTileY,
                npcType,
                occupants);
            if (current.IsValid)
            {
                selectedCandidate = candidate;
                placement = current;
                return true;
            }

            if (current.Result is VanillaHousingValidationResult.MissingFurniture or
                VanillaHousingValidationResult.MissingOrUnsafeWall or
                VanillaHousingValidationResult.RoomTooSmall or
                VanillaHousingValidationResult.RoomTooBig or
                VanillaHousingValidationResult.StartedInSolidTile)
            {
                canonicalHomes.Remove(Pack(candidate.HomeTileX, candidate.HomeTileY));
                candidates.RemoveAt(i--);
            }
        }

        selectedCandidate = default;
        placement = default;
        return false;
    }

    private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;
}
