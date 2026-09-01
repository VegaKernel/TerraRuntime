using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTruffleHousing1458Tests
{
    [Fact]
    public void Source_mushroom_threshold_is_required_for_truffle_room()
    {
        WorldTileStore enough = CreateRoom(top: 40, mushroomTiles: 100, worldSurface: 80d);
        VanillaHousingPlacement accepted = new VanillaHousingValidator1458(enough).Validate(25, 45, VanillaNpcIds.Truffle);
        Assert.NotEqual(VanillaHousingValidationResult.SpecialNpcConditionFailed, accepted.Result);

        WorldTileStore shortByOne = CreateRoom(top: 40, mushroomTiles: 99, worldSurface: 80d);
        Assert.Equal(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            new VanillaHousingValidator1458(shortByOne).Validate(25, 45, VanillaNpcIds.Truffle).Result);
    }

    [Fact]
    public void Persisted_unlock_allows_below_surface_room_but_keeps_mushroom_gate()
    {
        WorldTileStore tiles = CreateRoom(top: 100, mushroomTiles: 100, worldSurface: 80d);
        var locked = new VanillaHousingValidator1458(tiles);
        Assert.Equal(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            locked.Validate(25, 105, VanillaNpcIds.Truffle).Result);

        var unlocked = new VanillaHousingValidator1458(tiles);
        unlocked.SetTruffleUnlocked(true);
        Assert.NotEqual(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            unlocked.Validate(25, 105, VanillaNpcIds.Truffle).Result);
    }

    [Fact]
    public void No_functional_surface_matches_source_exception()
    {
        WorldTileStore tiles = CreateRoom(top: 70, mushroomTiles: 100, worldSurface: 30d);
        Assert.NotEqual(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            new VanillaHousingValidator1458(tiles).Validate(25, 75, VanillaNpcIds.Truffle).Result);
    }

    [Fact]
    public void World_projection_and_progression_journal_keep_truffle_unlock_explicit()
    {
        var metadata = new WorldFileRuntimeMetadata { UnlockedTruffleSpawn = true };
        VanillaTownSpawnWorldFacts1458 facts = RuntimeTownNpcWorldFactsProjection1458.FromMetadata(metadata);
        Assert.True(facts.UnlockedTruffleSpawn);

        var mutations = new RuntimeWorldProgressionMutations();
        mutations.SetTruffleSpawnBaseline(false);
        Assert.True(mutations.MarkTruffleSpawnUnlocked());
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        Assert.True(snapshot.UnlockTruffleSpawn);
        Assert.True(snapshot.HasAny);
    }

    private static WorldTileStore CreateRoom(int top, int mushroomTiles, double worldSurface)
    {
        var tiles = new WorldTileStore(new WorldDimensions(160, 160));
        Assert.True(tiles.TryAttachWorldSurface(worldSurface));
        const int left = 20;
        const int right = 31;
        int bottom = top + 9;
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
        {
            bool boundary = x == left || x == right || y == top || y == bottom;
            tiles.Set(x, y, new WorldTile
            {
                Type = boundary ? (ushort)1 : (ushort)0,
                Wall = 1,
                Flags = boundary ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }
        Place(tiles, 22, top + 6, 15);
        Place(tiles, 24, top + 6, 14);
        Place(tiles, 26, top + 3, 4);
        Place(tiles, 28, top + 5, 10);

        int written = 0;
        for (int x = 40; x < 50 && written < mushroomTiles; x++)
        for (int y = top; y < top + 10 && written < mushroomTiles; y++)
        {
            Place(tiles, x, y, (ushort)((written % 4) switch { 0 => 70, 1 => 71, 2 => 72, _ => 528 }));
            written++;
        }
        Assert.Equal(mushroomTiles, written);
        return tiles;
    }

    private static void Place(WorldTileStore tiles, int x, int y, ushort type) =>
        tiles.Set(x, y, new WorldTile { Type = type, Wall = 1, Flags = WorldTileFlags.Active });
}
