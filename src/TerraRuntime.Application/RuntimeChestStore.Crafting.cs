using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestStore
{
    private ulong craftingRevision;

    internal CraftingPreparation PrepareCrafting(ConnectionHandle connection,
        TerrariaCraftingRequest1458 request, WorldTileStore tiles, int maximumChanges) =>
        new(this, connection, request, tiles, maximumChanges);

    internal sealed class CraftingPreparation
    {
        private readonly RuntimeChestStore owner;
        private readonly ConnectionHandle connection;
        private readonly TerrariaCraftingRequest1458 request;
        private readonly WorldTileStore tiles;
        private readonly ulong revision;
        private readonly int maximumChanges;
        private readonly Dictionary<short, ChestImage> images = [];
        private readonly List<Change> changes = [];
        private readonly Dictionary<WorldSectionId, long> sectionVersions = [];

        internal CraftingPreparation(RuntimeChestStore owner, ConnectionHandle connection,
            TerrariaCraftingRequest1458 request, WorldTileStore tiles, int maximumChanges)
        {
            this.owner = owner;
            this.connection = connection;
            this.request = request;
            this.tiles = tiles;
            this.maximumChanges = maximumChanges;
            revision = owner.craftingRevision;
        }

        internal readonly record struct FundedMaterialLeg(short ChestId, byte Slot, WorldChestItem Before, WorldChestItem After)
        {
            internal int Amount => Before.Stack - After.Stack;
            internal int FundedQuantity => Before.TryGetItemType(out ItemTypeId type) && VanillaCoinFacts.TryGetValue(type, out _) ? 0 : Amount;
            internal long FundedCopper => Before.TryGetItemType(out ItemTypeId type) && VanillaCoinFacts.TryGetValue(type, out long unit) ? checked(unit * Amount) : 0;
        }

        internal bool TryCaptureFundedMaterialLegs(out FundedMaterialLeg[] legs)
        {
            legs = [];
            if (!owner.ownsLoadedWorldStock || !Accepted || !IsCurrent || changes.Count > 255) return false;
            var current = new Dictionary<(short Chest, byte Slot), WorldChestItem>();
            var result = new FundedMaterialLeg[changes.Count];
            for (int index = 0; index < changes.Count; index++)
            {
                Change change = changes[index];
                var key = (change.Chest, change.Slot);
                if (!current.TryGetValue(key, out WorldChestItem before)) before = images[change.Chest].Before[change.Slot];
                WorldChestItem after = change.After;
                if (before.IsEmpty || !before.TryGetItemType(out ItemTypeId type) || type.IsNone || before.Stack <= after.Stack ||
                    (!after.IsEmpty && (after.ItemType != before.ItemType || after.Prefix != before.Prefix))) return false;
                result[index] = new(change.Chest, change.Slot, before, after);
                current[key] = after;
            }
            legs = result;
            return true;
        }

        internal bool Accepted { get; private set; }
        internal bool IsCurrent => revision < ulong.MaxValue && owner.craftingRevision == revision &&
            sectionVersions.All(pair => tiles.GetSectionVersion(pair.Key) == pair.Value);

        internal IEnumerable<int> Prepare()
        {
            // One global retained operation, not one full-world capture per connection.
            // Later requests remain in the existing authoritative source mailboxes.
            short[] chestIds = request.Chests.ToArray();
            var distinct = new HashSet<short>();
            foreach (short chestId in chestIds)
            {
                yield return 1;
                if (!distinct.Add(chestId)) yield break;
            }

            TerrariaCraftingIngredient1458[] requirements = request.Ingredients.ToArray();
            foreach (TerrariaCraftingIngredient1458 requirement in requirements)
            {
                yield return 1;
                if (!TryResolveIngredient(in requirement, out var ingredient)) yield break;
                int remaining = requirement.Amount;
                foreach (short chestId in chestIds)
                {
                    yield return 1;
                    if (!images.TryGetValue(chestId, out ChestImage? image))
                    {
                        if (owner.chests[chestId] is not WorldChest chest ||
                            (owner.owners[chestId].IsAssigned && owner.owners[chestId] != connection) ||
                            (uint)chest.X >= (uint)tiles.Dimensions.WidthTiles ||
                            (uint)chest.Y >= (uint)tiles.Dimensions.HeightTiles)
                            continue;
                        WorldSectionId section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, chest.X, chest.Y);
                        sectionVersions.TryAdd(section, tiles.GetSectionVersion(section));
                        WorldTile tile = tiles.Get(chest.X, chest.Y);
                        if (IsLocked(in tile)) continue;
                        image = new(chest, tile);
                        images.Add(chestId, image);
                        for (int slot = 0; slot < chest.Items.Length; slot++)
                        {
                            image.Before[slot] = image.After[slot] = chest.Items[slot];
                            yield return 1;
                        }
                    }

                    for (int slot = 0; slot < image.After.Length && remaining > 0; slot++)
                    {
                        yield return 1;
                        WorldChestItem before = image.After[slot];
                        if (before.IsEmpty || !before.TryGetItemType(out ItemTypeId type) ||
                            !VanillaRecipeCatalog1458.IngredientMatches(in ingredient, type))
                            continue;
                        int amount = Math.Min(before.Stack, remaining);
                        WorldChestItem after = amount == before.Stack
                            ? default : before with { Stack = before.Stack - amount };
                        if (after.Stack > short.MaxValue || after.ItemType > short.MaxValue) yield break;
                        image.After[slot] = after;
                        // Preserve source requirement/chest/slot publication order, even
                        // when two valid requirements legitimately debit the same slot.
                        if (changes.Count >= maximumChanges) yield break;
                        changes.Add(new(chestId, checked((byte)slot), after));
                        remaining -= amount;
                    }
                    if (remaining == 0) break;
                }
                // Sequential reservation includes earlier concrete/group requests.
                // Original HandleRequest independently checks their original counts,
                // which can approve overlapping underpayment; that is not copied.
                if (remaining != 0) yield break;
            }

            foreach ((short chestId, ChestImage image) in images)
            {
                yield return 1;
                if (!ReferenceEquals(owner.chests[chestId], image.Chest) ||
                    owner.owners[chestId].IsAssigned && owner.owners[chestId] != connection ||
                    !tiles.Get(image.Chest.X, image.Chest.Y).Equals(image.Tile)) yield break;
                for (int slot = 0; slot < image.Before.Length; slot++)
                {
                    yield return 1;
                    if (image.Chest.Items[slot] != image.Before[slot]) yield break;
                }
            }
            Accepted = IsCurrent;
        }

        internal TerrariaChestItemState[] CaptureChanges() => changes.Select(change =>
            new TerrariaChestItemState(change.Chest, change.Slot, checked((short)change.After.Stack),
                change.After.Prefix, checked((short)change.After.ItemType))).ToArray();

        internal bool TryAdoptUnpublished()
        {
            if (!Accepted || !IsCurrent || owner.craftingRevision == ulong.MaxValue) return false;
            // All validation above is charged to the shared iterator budget. This
            // non-callback write section runs on the same authoritative thread.
            foreach (Change change in changes)
                images[change.Chest].Chest.Items[change.Slot] = change.After;
            owner.craftingRevision++;
            Accepted = false;
            return true;
        }

        private static bool TryResolveIngredient(in TerrariaCraftingIngredient1458 input,
            out VanillaRecipeIngredient1458 ingredient)
        {
            ingredient = default;
            if (input.Amount <= 0) return false;
            if (VanillaItemIds.TryCreate(input.ItemIdOrRecipeGroup, out ItemTypeId type) && !type.IsNone)
            {
                ingredient = new(type, input.Amount, input.ItemIdOrRecipeGroup);
                return true;
            }
            for (int groupId = 0; groupId < VanillaRecipeCatalog1458.GroupCount; groupId++)
            {
                if (VanillaRecipeCatalog1458.TryGetGroup(groupId, out var group) &&
                    group.FakeItemId == input.ItemIdOrRecipeGroup)
                {
                    ingredient = new(group.DecraftItem, input.Amount, input.ItemIdOrRecipeGroup);
                    return true;
                }
            }
            return false;
        }

        // Official Chest.IsLocked; the complete anchor tile is retained for late
        // mutation rejection. No client NearbyChests distance rule is invented.
        private static bool IsLocked(in WorldTile tile)
        {
            if (tile.TileType == VanillaTileIds.Containers)
                return tile.FrameX is >= 72 and <= 106 or >= 144 and <= 178 or
                    >= 828 and <= 1006 or >= 1296 and <= 1330 or >= 1368 and <= 1402 or
                    >= 1440 and <= 1474;
            return tile.TileType == VanillaTileIds.Containers2 && tile.FrameX / 36 == 13;
        }

        private sealed class ChestImage(WorldChest chest, WorldTile tile)
        {
            internal WorldChest Chest { get; } = chest;
            internal WorldTile Tile { get; } = tile;
            internal WorldChestItem[] Before { get; } = new WorldChestItem[chest.Items.Length];
            internal WorldChestItem[] After { get; } = new WorldChestItem[chest.Items.Length];
        }

        private readonly record struct Change(short Chest, byte Slot, WorldChestItem After);
    }
}
