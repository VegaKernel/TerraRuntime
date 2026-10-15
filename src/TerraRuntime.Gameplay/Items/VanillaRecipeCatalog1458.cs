using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.Gameplay.Items;

[Flags]
public enum VanillaRecipeFlags1458 : ushort
{
    NeedHoney = 1,
    NeedWater = 2,
    NeedLava = 4,
    NeedTorchGodsFavor = 8,
    Alchemy = 16,
    NeedSnowBiome = 32,
    NeedGraveyardBiome = 64,
    NeedMechdusa = 128,
    NotDecraftable = 256,
    Crimson = 512,
    Corruption = 1024,
}

public readonly record struct VanillaRecipeDefinition1458(int Index, ItemTypeId ResultType, int ResultStack,
    byte ResultPrefix, int ResultMaximumStack, int ResultValue, int RequiredTile, VanillaRecipeFlags1458 Flags);
public readonly record struct VanillaRecipeIngredient1458(ItemTypeId OriginalType, int Amount, int ItemIdOrGroup)
{
    public bool IsGroup => ItemIdOrGroup >= 1_000_000;
}
public readonly record struct VanillaRecipeGroup1458(int Id, int FakeItemId, ItemTypeId DecraftItem);

/// <summary>Immutable numeric results of the official 1.4.5.8 SetupRecipeGroups/SetupRecipes calls.
/// Catalog metadata alone does not authorize crafting. Crimson/corruption and decrafting flags
/// are retained metadata, not additional PlayerMeetsEnvironmentConditions gates.</summary>
public static partial class VanillaRecipeCatalog1458
{
    public const int Count = 3603;
    public const int GroupCount = 34;
    public const int IngredientLimit = 15;
    private const int RecipeStride = 13;
    public static bool TryGet(int index, out VanillaRecipeDefinition1458 recipe)
    {
        recipe = default;
        if ((uint)index >= Count) return false;
        int p = index * RecipeStride;
        recipe = new(index, new(Recipes[p]), Recipes[p+1], (byte)Recipes[p+2], Recipes[p+3], Recipes[p+4], Recipes[p+5], (VanillaRecipeFlags1458)Recipes[p+6]);
        return true;
    }
    public static int GetIngredientCount(int recipeIndex) => (uint)recipeIndex < Count ? Recipes[recipeIndex*RecipeStride+8] : 0;
    public static bool TryGetIngredient(int recipeIndex, int ingredientIndex, out VanillaRecipeIngredient1458 ingredient)
    {
        ingredient = default;
        if ((uint)recipeIndex >= Count || (uint)ingredientIndex >= GetIngredientCount(recipeIndex)) return false;
        int p = Recipes[recipeIndex*RecipeStride+7] + ingredientIndex*3;
        ingredient = new(new(Ingredients[p]), Ingredients[p+1], Ingredients[p+2]);
        return true;
    }
    public static ReadOnlySpan<int> GetAcceptedGroups(int recipeIndex) => (uint)recipeIndex < Count
        ? AcceptedGroups.AsSpan(Recipes[recipeIndex*RecipeStride+9],Recipes[recipeIndex*RecipeStride+10]) : [];
    public static ReadOnlySpan<int> GetCustomShimmerPairs(int recipeIndex) => (uint)recipeIndex < Count
        ? Shimmer.AsSpan(Recipes[recipeIndex*RecipeStride+11],Recipes[recipeIndex*RecipeStride+12]*2) : [];
    public static bool TryGetGroup(int groupId, out VanillaRecipeGroup1458 group)
    {
        group = default;
        if ((uint)groupId >= GroupCount) return false;
        int p = groupId*7; group = new(Groups[p],Groups[p+1],new(Groups[p+2])); return true;
    }
    public static ReadOnlySpan<int> GetGroupMembers(int groupId) => (uint)groupId < GroupCount
        ? GroupMembers.AsSpan(Groups[groupId*7+3],Groups[groupId*7+4]) : [];
    public static ReadOnlySpan<int> GetOrderedGroupMembers(int groupId) => (uint)groupId < GroupCount
        ? GroupOrderedMembers.AsSpan(Groups[groupId*7+5],Groups[groupId*7+6]) : [];
    public static bool IngredientMatches(in VanillaRecipeIngredient1458 ingredient, ItemTypeId item)
        => ingredient.IsGroup ? GetGroupMembers(ingredient.ItemIdOrGroup-1_000_000).BinarySearch(item.Value)>=0 : ingredient.ItemIdOrGroup==item.Value;
    public static ReadOnlySpan<int> GetStationAliases(int tile)
    {
        for(int i=0;i<Aliases.Length;i+=3) if(Aliases[i]==tile) return AliasMembers.AsSpan(Aliases[i+1],Aliases[i+2]);
        return [];
    }
    public static bool CountsAsWater(int tile) => WaterTiles.AsSpan().BinarySearch(tile)>=0;
    public static bool IsUsedStation(int tile) => (uint)tile < TilesUsed.Length && TilesUsed[tile]!=0;
}
