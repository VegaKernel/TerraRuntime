using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct RecipeEnvironment1458(
    bool Water, bool Honey, bool Lava, bool AlchemyTable,
    bool Snow, bool Graveyard, bool TorchGod, bool Mechdusa);

public static partial class VanillaRecipeCatalog1458
{
    public const int CraftingTileCount = 754;

    /// <summary>Accumulates one observed tile in Player.AdjTiles' inclusive region.
    /// The authoritative caller supplies the clamped region and clears its station
    /// image and transient liquid/alchemy fields before scanning.</summary>
    public static bool TryAccumulateCraftingTile(int type, bool active, int liquid, int kind,
        Span<bool> stations, ref RecipeEnvironment1458 environment)
    {
        if ((uint)type >= CraftingTileCount || (uint)liquid > byte.MaxValue ||
            (uint)kind > 3 || stations.Length != CraftingTileCount) return false;

        bool water = environment.Water;
        bool alchemy = environment.AlchemyTable;
        if (active)
        {
            // Captured 1.4.5.8 inheritance is acyclic. A bounded visited image also
            // prevents duplicate aliases from changing the work bound.
            Span<int> pending = stackalloc int[CraftingTileCount];
            Span<bool> visited = stackalloc bool[CraftingTileCount];
            visited.Clear();
            int count = 1;
            pending[0] = type;
            while (count > 0)
            {
                int tile = pending[--count];
                if (visited[tile]) continue;
                visited[tile] = true;
                stations[tile] = true;
                if (tile is 355 or 699) alchemy = true;
                foreach (int alias in GetStationAliases(tile))
                    if (!visited[alias]) pending[count++] = alias;
            }
            // AdjTiles checks this set on the actual tile, not inherited stations.
            water |= CountsAsWater(type);
        }
        bool enough = liquid > 200;
        environment = environment with
        {
            Water = water || (enough && kind == 0),
            Honey = environment.Honey || (enough && kind == 2),
            Lava = environment.Lava || (enough && kind == 1),
            AlchemyTable = alchemy
        };
        return true;
    }

    /// <summary>Recipe.PlayerMeetsEnvironmentConditions, 1.4.5.8. Alchemy affects
    /// ingredient discounts; crimson/corruption and decrafting flags are metadata.</summary>
    public static bool MeetsEnvironment(int recipeIndex, ReadOnlySpan<bool> stations,
        in RecipeEnvironment1458 environment)
    {
        if (stations.Length != CraftingTileCount || !TryGet(recipeIndex, out var recipe)) return false;
        if (recipe.RequiredTile >= 0 && !stations[recipe.RequiredTile]) return false;
        var flags = recipe.Flags;
        return (!flags.HasFlag(VanillaRecipeFlags1458.NeedWater) || environment.Water) &&
               (!flags.HasFlag(VanillaRecipeFlags1458.NeedHoney) || environment.Honey) &&
               (!flags.HasFlag(VanillaRecipeFlags1458.NeedLava) || environment.Lava) &&
               (!flags.HasFlag(VanillaRecipeFlags1458.NeedSnowBiome) || environment.Snow) &&
               (!flags.HasFlag(VanillaRecipeFlags1458.NeedGraveyardBiome) || environment.Graveyard) &&
               (!flags.HasFlag(VanillaRecipeFlags1458.NeedMechdusa) || environment.Mechdusa) &&
               (!flags.HasFlag(VanillaRecipeFlags1458.NeedTorchGodsFavor) || environment.TorchGod);
    }
}
