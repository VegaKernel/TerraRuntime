using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct WorldItemCraftLeg1458(short SourceSlot, int IngredientIndex,
    WorldItemPickupSlot1458 Before, WorldItemPickupSlot1458 After, int Amount);
public readonly record struct WorldItemCraftExternalLeg1458(int MaterialIndex, int IngredientIndex,
    WorldItemPickupSlot1458 Item, int Amount);
public sealed record WorldItemCraftPlan1458(int RecipeIndex, WorldItemPickupSlot1458 Result,
    WorldItemPickupSlot1458[] After, WorldItemCraftLeg1458[] NominalReservations,
    WorldItemCraftLeg1458[] Debits, WorldItemStorageMutation1458[] StorageGrants,
    WorldItemPickupSlot1458 StorageResidual, short KillOwnedProjectileType)
{
    public WorldItemCraftExternalLeg1458[] ExternalDebits { get; init; } = [];
}

public static partial class VanillaWorldItemOwner1458
{
    private const int CraftBankCount = 4;
    private const int CraftBankSlots = 40;
    private const int CraftSnapshotSlots = VanillaPlayerItemSlotCatalog.InventoryCount + CraftBankCount * CraftBankSlots;
    private const int CraftShellphoneDummy = 5437;
    private const int CraftShellphone = 5358;
    private const int CraftTrustyCattiva = 6148;
    private const int CraftTrustyFoxsparks = 6149;
    private const short CraftCattivaProjectile = 1093;
    private const short CraftFoxsparksProjectile = 1094;

    /// <summary>One detached source craft, with client RNG outcomes supplied as a certified physical witness.
    /// This validates possible discounts/prefixes but does not own or advance a client's RNG.
    /// The caller proves accessible container order, complete observed image and private custody convergence.</summary>
    public static bool TryPlanCraftWitness(int recipeIndex, ReadOnlySpan<WorldItemPickupSlot1458> inventoryAndBanks,
        ReadOnlySpan<byte> accessibleBankOrder, ReadOnlySpan<bool> adjacentTiles, in RecipeEnvironment1458 environment,
        bool quickCraft, bool pendingInventoryAction, ReadOnlySpan<int> ingredientDiscounts, byte resultPrefix,
        bool windowsItemPrefixArithmetic, VanillaItemPrefixWorld1458? world, out WorldItemCraftPlan1458? plan)
        => TryPlanCraftWitnessCore(recipeIndex, inventoryAndBanks, accessibleBankOrder, adjacentTiles, in environment,
            quickCraft, pendingInventoryAction, ingredientDiscounts, resultPrefix, windowsItemPrefixArithmetic, world,
            [], out plan);

    /// <summary>Local stock is consumed first. External items are exact already-removed source world material,
    /// not extra player slots or evidence of a recipe by themselves. The caller validates their paid custody
    /// and requires the complete certificate to be consumed once by the final observed recipe history.</summary>
    public static bool TryPlanCraftWitnessWithExternalMaterials(int recipeIndex,
        ReadOnlySpan<WorldItemPickupSlot1458> inventoryAndBanks, ReadOnlySpan<byte> accessibleBankOrder,
        ReadOnlySpan<bool> adjacentTiles, in RecipeEnvironment1458 environment, bool quickCraft,
        byte resultPrefix, bool windowsItemPrefixArithmetic, VanillaItemPrefixWorld1458? world,
        ReadOnlySpan<WorldItemPickupSlot1458> externalMaterials, out WorldItemCraftPlan1458? plan)
        => TryPlanCraftWitnessCore(recipeIndex, inventoryAndBanks, accessibleBankOrder, adjacentTiles, in environment,
            quickCraft, false, new int[VanillaRecipeCatalog1458.GetIngredientCount(recipeIndex)], resultPrefix,
            windowsItemPrefixArithmetic, world, externalMaterials, out plan);

    private static bool TryPlanCraftWitnessCore(int recipeIndex, ReadOnlySpan<WorldItemPickupSlot1458> inventoryAndBanks,
        ReadOnlySpan<byte> accessibleBankOrder, ReadOnlySpan<bool> adjacentTiles, in RecipeEnvironment1458 environment,
        bool quickCraft, bool pendingInventoryAction, ReadOnlySpan<int> ingredientDiscounts, byte resultPrefix,
        bool windowsItemPrefixArithmetic, VanillaItemPrefixWorld1458? world,
        ReadOnlySpan<WorldItemPickupSlot1458> externalMaterials, out WorldItemCraftPlan1458? plan)
    {
        plan = null;
        if (pendingInventoryAction || inventoryAndBanks.Length != CraftSnapshotSlots || accessibleBankOrder.Length > CraftBankCount ||
            !VanillaRecipeCatalog1458.TryGet(recipeIndex, out var recipe) ||
            !VanillaRecipeCatalog1458.MeetsEnvironment(recipeIndex, adjacentTiles, in environment) ||
            ingredientDiscounts.Length != VanillaRecipeCatalog1458.GetIngredientCount(recipeIndex)) return false;
        if (externalMaterials.Length > 255) return false;
        foreach (var item in externalMaterials)
            if (!IsCanonicalStorageSlot(in item)) return false;
        // This overload does not certify a client alchemy lottery or nominal all-skip stock.
        if (!externalMaterials.IsEmpty && environment.AlchemyTable &&
            (recipe.Flags & VanillaRecipeFlags1458.Alchemy) != 0) return false;
        byte banksSeen=0;
        foreach(byte bank in accessibleBankOrder)
        {
            if(bank>=CraftBankCount || (banksSeen & (1<<bank))!=0) return false;
            banksSeen|=(byte)(1<<bank);
        }
        foreach(var slot in inventoryAndBanks) if(!IsCanonicalStorageSlot(in slot)) return false;
        var cursor=inventoryAndBanks[VanillaPlayerItemSlotCatalog.InventoryMouseItem];
        if(cursor.Type!=0 && (CanCraftItemHavePrefixes(cursor.Type,world) || cursor.Type!=recipe.ResultType.Value ||
            cursor.Prefix!=recipe.ResultPrefix || cursor.Stack+recipe.ResultStack>recipe.ResultMaximumStack)) return false;
        bool discountsAllowed=(recipe.Flags & VanillaRecipeFlags1458.Alchemy)!=0 && environment.AlchemyTable;
        var nominal=inventoryAndBanks.ToArray();var after=inventoryAndBanks.ToArray();
        var reservations=new List<WorldItemCraftLeg1458>();var debits=new List<WorldItemCraftLeg1458>();
        var external = externalMaterials.ToArray();
        var nominalExternal = externalMaterials.ToArray();
        var externalDebits = new List<WorldItemCraftExternalLeg1458>();
        for(int i=0;i<ingredientDiscounts.Length;i++)
        {
            if(!VanillaRecipeCatalog1458.TryGetIngredient(recipeIndex,i,out var ingredient) ||
                ingredientDiscounts[i]<0 || ingredientDiscounts[i]>ingredient.Amount ||
                (!discountsAllowed && ingredientDiscounts[i]!=0) ||
                !ConsumeCraftIngredientWithExternal(nominal,accessibleBankOrder,in ingredient,i,ingredient.Amount,reservations,
                    nominalExternal, null) ||
                !ConsumeCraftIngredientWithExternal(after,accessibleBankOrder,in ingredient,i,ingredient.Amount-ingredientDiscounts[i],debits,
                    external, externalDebits)) return false;
        }
        // Item.OnCreated(RecipeItemCreationContext) is distinct from SetDefaults and currently has one identity transform.
        int producedType=recipe.ResultType.Value==CraftShellphoneDummy?CraftShellphone:recipe.ResultType.Value;
        if(!VanillaWorldItemAllocationCatalog1458.TryGet(producedType,out var resultFacts) ||
            !IsPossibleCraftPrefix(producedType,recipe.ResultStack,resultPrefix,windowsItemPrefixArithmetic,world)) return false;
        var born=new WorldItemPickupSlot1458(producedType,recipe.ResultStack,resultPrefix,false);
        if(cursor.Type!=0 && (cursor.Type!=born.Type || cursor.Prefix!=born.Prefix)) return false;
        var granted=born with {Stack=checked(born.Stack+cursor.Stack)};
        if(granted.Stack>resultFacts.MaximumStack) return false;
        var storage=Array.Empty<WorldItemStorageMutation1458>();var residual=granted;
        if(quickCraft)
        {
            if(!TryPlanStorage(in granted,after.AsSpan(0,VanillaPlayerItemSlotCatalog.InventoryCount),false,world,out var stored, true) || stored is null) return false;
            storage=stored.Mutations;
            foreach(var mutation in storage) after[mutation.Slot]=mutation.After;
            residual=stored.Residual;
        }
        after[VanillaPlayerItemSlotCatalog.InventoryMouseItem]=residual;
        short projectile=producedType switch {CraftTrustyCattiva=>CraftCattivaProjectile,CraftTrustyFoxsparks=>CraftFoxsparksProjectile,_=>0};
        plan=new(recipeIndex,born,after,reservations.ToArray(),debits.ToArray(),storage,residual,projectile)
        { ExternalDebits = externalDebits.ToArray() };
        return true;
    }

    /// <summary>One source craft on an explicitly owned random stream. Availability is checked before any draw.
    /// This API is for source-exact deterministic consumers; observational client certification uses the witness API.</summary>
    public static bool TryPlanCraftWithRandom(int recipeIndex, ReadOnlySpan<WorldItemPickupSlot1458> inventoryAndBanks,
        ReadOnlySpan<byte> accessibleBankOrder, ReadOnlySpan<bool> adjacentTiles, in RecipeEnvironment1458 environment,
        bool quickCraft, bool pendingInventoryAction, Func<int, int> next, bool windowsItemPrefixArithmetic,
        VanillaItemPrefixWorld1458? world, out WorldItemCraftPlan1458? plan)
        => TryPlanCraftWithRandom(recipeIndex, inventoryAndBanks, accessibleBankOrder, adjacentTiles, in environment,
            quickCraft, pendingInventoryAction, next, windowsItemPrefixArithmetic, world, out plan, out _);

    /// <summary>The caller supplies a detached RNG cursor and adopts it only on Resolved.
    /// BudgetExhausted is an explicit bounded policy result, not a fabricated no-prefix source outcome.</summary>
    public static bool TryPlanCraftWithRandom(int recipeIndex, ReadOnlySpan<WorldItemPickupSlot1458> inventoryAndBanks,
        ReadOnlySpan<byte> accessibleBankOrder, ReadOnlySpan<bool> adjacentTiles, in RecipeEnvironment1458 environment,
        bool quickCraft, bool pendingInventoryAction, Func<int, int> next, bool windowsItemPrefixArithmetic,
        VanillaItemPrefixWorld1458? world, out WorldItemCraftPlan1458? plan, out VanillaItemPrefixNormalizationStatus1458 status)
    {
        ArgumentNullException.ThrowIfNull(next);
        plan = null;
        status = VanillaItemPrefixNormalizationStatus1458.Unsupported;
        if (!VanillaRecipeCatalog1458.TryGet(recipeIndex, out var recipe)) return false;
        int type = recipe.ResultType.Value == CraftShellphoneDummy ? CraftShellphone : recipe.ResultType.Value;
        if (PrefixWorldRecords1458.HasVariant(type) && world is null) return false;
        int count = VanillaRecipeCatalog1458.GetIngredientCount(recipeIndex);
        Span<int> discounts = stackalloc int[count];
        discounts.Clear();
        if (!TryPlanCraftWitness(recipeIndex, inventoryAndBanks, accessibleBankOrder, adjacentTiles, in environment, quickCraft,
            pendingInventoryAction, discounts, 0, windowsItemPrefixArithmetic, world, out _)) return false;
        if ((recipe.Flags & VanillaRecipeFlags1458.Alchemy) != 0 && environment.AlchemyTable)
            for (int i = 0; i < count; i++)
            {
                VanillaRecipeCatalog1458.TryGetIngredient(recipeIndex, i, out var ingredient);
                for (int unit = 0; unit < ingredient.Amount; unit++)
                {
                    int offer = next(3);
                    if ((uint)offer >= 3)
                    {
                        status = VanillaItemPrefixNormalizationStatus1458.InvalidDraw;
                        return false;
                    }
                    if (offer == 0) discounts[i]++;
                }
            }
        byte prefix = 0;
        status = recipe.ResultStack <= 1
            ? RollCraftPrefix(type, next, windowsItemPrefixArithmetic, world, out prefix)
            : VanillaItemPrefixNormalizationStatus1458.Resolved;
        return status == VanillaItemPrefixNormalizationStatus1458.Resolved &&
            TryPlanCraftWitness(recipeIndex, inventoryAndBanks, accessibleBankOrder, adjacentTiles, in environment, quickCraft,
                pendingInventoryAction, discounts, prefix, windowsItemPrefixArithmetic, world, out plan);
    }

    private static bool ConsumeCraftIngredientWithExternal(WorldItemPickupSlot1458[] slots, ReadOnlySpan<byte> banks,
        in VanillaRecipeIngredient1458 ingredient, int ingredientIndex, int amount, List<WorldItemCraftLeg1458> legs,
        WorldItemPickupSlot1458[] external, List<WorldItemCraftExternalLeg1458>? externalLegs)
    {
        int remaining = amount;
        for (int slot = 0; slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount && remaining > 0; slot++)
            ConsumeCraftSlot(slots, slot, in ingredient, ingredientIndex, legs, ref remaining);
        foreach (byte bank in banks)
            for (int slot = VanillaPlayerItemSlotCatalog.InventoryCount + bank * CraftBankSlots;
                slot < VanillaPlayerItemSlotCatalog.InventoryCount + (bank + 1) * CraftBankSlots && remaining > 0; slot++)
                ConsumeCraftSlot(slots, slot, in ingredient, ingredientIndex, legs, ref remaining);
        for (int index = 0; index < external.Length && remaining > 0; index++)
        {
            var item = external[index];
            var itemType = new ItemTypeId(item.Type);
            if (itemType.IsNone || !VanillaRecipeCatalog1458.IngredientMatches(in ingredient, itemType)) continue;
            int taken = Math.Min(item.Stack, remaining);
            remaining -= taken;
            external[index] = taken == item.Stack ? default : item with { Stack = item.Stack - taken };
            externalLegs?.Add(new(index, ingredientIndex, item, taken));
        }
        return remaining == 0;
    }

    private static void ConsumeCraftSlot(WorldItemPickupSlot1458[] slots,int slot,in VanillaRecipeIngredient1458 ingredient,
        int ingredientIndex,List<WorldItemCraftLeg1458> legs,ref int remaining)
    {
        var before=slots[slot];
        if(before.Type==0 || !VanillaRecipeCatalog1458.IngredientMatches(in ingredient,new(before.Type)))return;
        int amount=Math.Min(remaining,before.Stack);remaining-=amount;
        var after=amount==before.Stack?default:before with {Stack=before.Stack-amount};
        slots[slot]=after;legs.Add(new((short)slot,ingredientIndex,before,after,amount));
    }
    private static bool CanCraftItemHavePrefixes(int type,VanillaItemPrefixWorld1458? world)
        => PrefixWorldRecords1458.HasVariant(type)
            ? world is not { } context || PrefixWorldRecords1458.TryGet(new(type),context,out _)
            : VanillaItemPrefixTable1458.TryGet(new(type),out _);
    private static bool IsPossibleCraftPrefix(int type,int stack,byte prefix,bool windows,VanillaItemPrefixWorld1458? world)
    {
        if(prefix==0)return true;
        if(stack>1 || prefix>=VanillaItemPrefixTable1458.PrefixCount)return false;
        VanillaItemPrefixRecord1458 record=default;
        bool has=PrefixWorldRecords1458.HasVariant(type)
            ? world is { } context && PrefixWorldRecords1458.TryGet(new(type),context,out record)
            : VanillaItemPrefixTable1458.TryGet(new(type),out record);
        return has && VanillaItemPrefixTable1458.GetFamily(record.Family).Contains(prefix) && VanillaItemPrefixTable1458.Accepts(in record,prefix,windows);
    }
    private static VanillaItemPrefixNormalizationStatus1458 RollCraftPrefix(int type, Func<int, int> next,
        bool windows, VanillaItemPrefixWorld1458? world, out byte prefix)
    {
        prefix = 0;
        if (PrefixWorldRecords1458.HasVariant(type) && world is null)
            return VanillaItemPrefixNormalizationStatus1458.Unsupported;
        VanillaItemPrefixRecord1458 record = default;
        bool has = PrefixWorldRecords1458.HasVariant(type)
            ? PrefixWorldRecords1458.TryGet(new(type), world!.Value, out record)
            : VanillaItemPrefixTable1458.TryGet(new(type), out record);
        if (!has) return VanillaItemPrefixNormalizationStatus1458.Resolved;
        var family = VanillaItemPrefixTable1458.GetFamily(record.Family);
        // Share the admitted normalization bound; a refused detached cursor is never adopted.
        for (int attempt = 0; attempt < VanillaItemPrefixNormalization1458.MaximumAttempts; attempt++)
        {
            int offer = next(4);
            if ((uint)offer >= 4) return VanillaItemPrefixNormalizationStatus1458.InvalidDraw;
            int candidate = 0;
            if (offer != 0)
            {
                int index = next(family.Length);
                if ((uint)index >= (uint)family.Length) return VanillaItemPrefixNormalizationStatus1458.InvalidDraw;
                candidate = family[index];
            }
            if (VanillaItemPrefixTable1458.HasReducedNaturalChance(candidate))
            {
                int reduced = next(3);
                if ((uint)reduced >= 3) return VanillaItemPrefixNormalizationStatus1458.InvalidDraw;
                if (reduced != 0) candidate = 0;
            }
            if (!VanillaItemPrefixTable1458.Accepts(in record, candidate, windows)) continue;
            prefix = (byte)candidate;
            return VanillaItemPrefixNormalizationStatus1458.Resolved;
        }
        return VanillaItemPrefixNormalizationStatus1458.BudgetExhausted;
    }
}
