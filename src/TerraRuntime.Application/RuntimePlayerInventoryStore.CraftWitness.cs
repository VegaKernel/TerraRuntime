using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    private static readonly Dictionary<int, int[]> craftRecipesByResult = BuildCraftRecipeIndex();
    private static Dictionary<int, int[]> BuildCraftRecipeIndex()
    {
        var groups = new Dictionary<int, List<int>>();
        for (int index = 0; index < VanillaRecipeCatalog1458.Count; index++)
        {
            if (!VanillaRecipeCatalog1458.TryGet(index, out var recipe)) continue;
            int type = recipe.ResultType.Value == 5437 ? 5358 : recipe.ResultType.Value;
            if (!groups.TryGetValue(type, out var list)) groups[type] = list = [];
            list.Add(index);
        }
        return groups.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray());
    }
    internal static IEnumerable<int> CraftRecipesForResult(int type) => craftRecipesByResult.GetValueOrDefault(type) ?? [];

    internal sealed class CraftWitnessResolution
    {
        internal ShopBankMovePreparation? Preparation;
        internal bool Ambiguous;
        internal bool HasUnconfirmedLottery;
        internal bool UniqueReducedAllocation = true;
        internal short KillOwnedProjectileType;
        internal bool ConsumesAllRemoteCosts;
    }

    // This iterator represents one source recipe repeated against a retained
    // image. Accessible orders come from the caller's source container proof.
    // It is not a general hidden-history uniqueness certificate.
    internal IEnumerable<int> PrepareCraftWitnessWork(ConnectionHandle connection, ShopJournalCapture capture,
        RuntimePlayerInventoryItem[] observed, byte[][] accessibleOrders, bool[] stations,
        RecipeEnvironment1458 environment, VanillaItemPrefixWorld1458? world, CraftWitnessResolution result,
        CraftExternalMaterial[]? remoteMaterials = null)
    {
        yield return 0;
        if (!capture.IsCurrent || observed.Length != 219 || stations.Length != 754) yield break;
        var before = capture.Before;
        var prefixes = new HashSet<(int Type, byte Prefix)>();
        for (int slot = 0; slot < 219; slot++)
        {
            yield return 0;
            var item = observed[slot];
            if (!item.IsEmpty && (before[slot].ItemType != item.ItemType || before[slot].Prefix != item.Prefix ||
                item.Stack > before[slot].Stack)) prefixes.Add((item.ItemType.Value, checked((byte)item.Prefix.Value)));
        }
        foreach (int index in prefixes.Select(static value => value.Type).Distinct().SelectMany(CraftRecipesForResult))
        {
            yield return 0;
            if (!VanillaRecipeCatalog1458.TryGet(index, out var recipe) ||
                !VanillaRecipeCatalog1458.MeetsEnvironment(index, stations, in environment)) continue;
            int produced = recipe.ResultType.Value == 5437 ? 5358 : recipe.ResultType.Value;
            if (!prefixes.Any(value => value.Type == produced)) continue;
            bool lottery = environment.AlchemyTable && (recipe.Flags & VanillaRecipeFlags1458.Alchemy) != 0;
            if (lottery) { result.HasUnconfirmedLottery = true; continue; }
            foreach (byte[] order in accessibleOrders)
                foreach (var prefix in prefixes.Where(value => value.Type == produced))
                    for (int mode = 0; mode < 4; mode++)
                    {
                        yield return 0;
                        var working = TryCreateCraftWorking(connection, capture);
                        if (working is null) yield break;
                        bool quick = (mode & 1) != 0, windows = (mode & 2) != 0;
                        var remote = remoteMaterials?.ToArray();
                        int targetCount = CountCraftIdentity(observed, produced, prefix.Prefix);
                        while (CountCraftIdentity(working.Image, produced, prefix.Prefix) < targetCount)
                        {
                            yield return 0;
                            int previousCount = CountCraftIdentity(working.Image, produced, prefix.Prefix);
                            var slots = working.Image.Select(static item => new WorldItemPickupSlot1458(item.ItemType.Value,
                                item.Stack, item.Prefix.Value, (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
                            WorldItemCraftPlan1458? plan;
                            bool planned = remote is null
                                ? VanillaWorldItemOwner1458.TryPlanCraftWitness(index, slots, order, stations,
                                    in environment, quick, false, new int[VanillaRecipeCatalog1458.GetIngredientCount(index)],
                                    prefix.Prefix, windows, world, out plan)
                                : VanillaWorldItemOwner1458.TryPlanCraftWitnessWithExternalMaterials(index, slots, order, stations,
                                    in environment, quick, prefix.Prefix, windows, world, remote.Select(static material => material.Item).ToArray(), out plan);
                            if (!planned || plan is null) break;
                            if (remote is not null)
                            {
                                bool paid = true;
                                var funding = new CraftExternalMaterial[plan.ExternalDebits.Length];
                                for (int legIndex = 0; legIndex < plan.ExternalDebits.Length; legIndex++)
                                {
                                    var leg = plan.ExternalDebits[legIndex];
                                    if ((uint)leg.MaterialIndex >= remote.Length || remote[leg.MaterialIndex].Item != leg.Item ||
                                        leg.Amount <= 0 || leg.Amount > leg.Item.Stack) { paid = false; break; }
                                    var material = remote[leg.MaterialIndex];
                                    int takenQuantity = Math.Min(material.Quantity, leg.Amount);
                                    long takenCopper = VanillaCoinFacts.TryGetValue(new(leg.Item.Type), out long unit)
                                        ? Math.Min(material.Copper, checked(unit * leg.Amount)) : 0;
                                    funding[legIndex] = new(leg.Item with { Stack = leg.Amount }, takenQuantity, takenCopper);
                                    remote[leg.MaterialIndex] = leg.Amount == leg.Item.Stack ? default :
                                        new(leg.Item with { Stack = leg.Item.Stack - leg.Amount },
                                            material.Quantity - takenQuantity, material.Copper - takenCopper);
                                }
                                if (!paid || !working.TryApply(plan, plan.ExternalDebits, funding)) break;
                            }
                            else if (!working.TryApply(plan)) break;
                            int count = CountCraftIdentity(working.Image, produced, prefix.Prefix);
                            if (count <= previousCount || count > targetCount) break;
                            if (!working.Image.AsSpan().SequenceEqual(observed) ||
                                (remote is not null && remote.Any(static material => material.Item.Type != 0))) continue;
                            var accepted = working.TryPrepareAccepted(observed);
                            if (accepted is null) break;
                            short killType = plan.KillOwnedProjectileType;
                            if (result.Preparation is { } prior && (!prior.HasSameAcceptedImage(accepted) || result.KillOwnedProjectileType != killType))
                            { result.Preparation = null; result.Ambiguous = true; yield break; }
                            result.UniqueReducedAllocation &= working.HasUniqueCraftAllocation();
                            result.KillOwnedProjectileType = killType;
                            result.ConsumesAllRemoteCosts = remote is not null;
                            result.Preparation ??= accepted;
                            break;
                        }
                    }
        }
    }

    private static int CountCraftIdentity(RuntimePlayerInventoryItem[] image, int type, byte prefix)
    {
        int count = 0;
        foreach (var item in image)
            if (item.ItemType.Value == type && item.Prefix.Value == prefix) count = checked(count + item.Stack);
        return count;
    }
}
