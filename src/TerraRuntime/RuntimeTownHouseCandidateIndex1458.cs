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

    public bool TryFindRoom(
        NpcTypeId npcType,
        ReadOnlySpan<VanillaHousingOccupant> occupants,
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

        placement = default;
        return false;
    }

    private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;
}
