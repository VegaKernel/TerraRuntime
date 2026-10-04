using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Owns detached source pickup eligibility from the exact live world, not socket/client item predictions.</summary>
internal sealed class RuntimeWorldItemOwnerFactsProvider1458(PlayerAuthority players, WorldTileStore? tiles,
    bool expertMode, bool masterMode) : IWorldItemOwnerFactsProvider1458
{
    public IWorldItemOwnerFactsSnapshot1458 Capture() => new Snapshot(players, tiles, expertMode, masterMode);

    private sealed class Snapshot : IWorldItemOwnerFactsSnapshot1458
    {
        private readonly PlayerAuthority owner;
        private readonly WorldTileStore? tiles;
        private readonly bool expert, master;
        private readonly RuntimeItemOwnerPlayerCapture1458[] players;
        private readonly List<(int X, int Y, bool Hopper)> terrain = new();

        public Snapshot(PlayerAuthority owner, WorldTileStore? tiles, bool expert, bool master)
        { this.owner = owner; this.tiles = tiles; this.expert = expert; this.master = master; players = owner.CaptureItemOwnerPlayers(); }

        public bool IsCurrent
        {
            get
            {
                if (!owner.IsCurrentItemOwnerPlayers(players)) return false;
                foreach (var cell in terrain)
                    if (tiles is null || IsHopper(tiles.Get(cell.X, cell.Y)) != cell.Hopper) return false;
                return true;
            }
        }

        public bool TrySelectOwner(in WorldItemDropStateUpdate drop, int grabDelay, byte grabPlayer, out byte selected)
        {
            selected = byte.MaxValue;
            if (drop.ShimmerTime > 0f || (grabDelay > 0 && grabPlayer == byte.MaxValue)) return true;
            if (!VanillaWorldItemPickupCatalog1458.TryGet(drop.ItemNetId, out var facts)) return false;
            var candidates = new WorldItemOwnerPlayer1458[players.Length];
            for (int index = 0; index < players.Length; index++)
            {
                var capture = players[index]; var state = capture.State;
                if (state.IsDead || (grabDelay > 0 && (grabPlayer == state.Player.Slot.Value || grabPlayer == byte.MaxValue))) continue;
                if (!capture.HasInventory) return false;
                var inventory = capture.Inventory.Select(static i => new WorldItemPickupSlot1458(i.ItemType.Value, i.Stack,
                    i.Prefix.Value, (i.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
                bool prevent = WorldItemPickupSlotExtensions1458.ContainsAnyType(inventory.AsSpan(0, VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount), 4346);
                // Dedicated remote Player.voidVaultInfo starts zero. Its setter is local-only (Player.Update,
                // i == Main.myPlayer), and no admitted network packet projects it. Inventory Void Bag alone
                // must not invent the client-local enable flag.
                if (!VanillaWorldItemOwner1458.TryCanPull(drop.ItemNetId, drop.Prefix, inventory, default,
                    voidEnabled: false, prevent, out bool canPull)) return false;
                if (!canPull) continue;
                bool mana = false, life = false, treasure = false, gold = false;
                // Favorited loadout sharing uses additional accessory-compatibility facts. Fence only a
                // represented pickup modifier that could replace an empty active accessory slot.
                foreach (var shared in capture.Equipment)
                {
                    if (shared.SlotId < 900 || shared.Stack <= 0 || (shared.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) == 0) continue;
                    int sharedSlot = (shared.SlotId - 900) % 30;
                    if (sharedSlot is < 3 or > 9 || shared.ItemNetId is not (2219 or 2220 or 2221 or 4000 or 5010 or 5126 or 3033 or 3034 or 3035)) continue;
                    if (!capture.Equipment.Any(i => i.SlotId == VanillaPlayerItemSlotCatalog.ArmorStart + sharedSlot && i.Stack > 0)) return false;
                }
                foreach (var item in capture.Equipment)
                {
                    if (!VanillaPlayerItemSlotCatalog.IsFunctionalArmorSlot(item.SlotId) || item.Stack <= 0) continue;
                    int armorSlot = item.SlotId - VanillaPlayerItemSlotCatalog.ArmorStart;
                    if (armorSlot < 3) continue; // UpdateEquips_CanItemGrantBenefits requires actual armor slots here.
                    if (armorSlot == 8 && (!expert || capture.Appearance is not { DifficultyFlags: var flags } || (flags & 4) == 0)) continue;
                    if (armorSlot == 9 && !master) continue;
                    mana |= item.ItemNetId is 2219 or 2220 or 2221 or 4000;
                    treasure |= item.ItemNetId is 5010 or 5126;
                    gold |= item.ItemNetId is 3033 or 3034 or 3035;
                }
                if (prevent && !facts.IgnoresEncumbering) continue;
                if (drop.ItemNetId is 58 or 1734 or 1867)
                {
                    // Fresh remote Player buff slots are zero; this generation's accepted packet-50 snapshot
                    // replaces them. A missing snapshot retains that owned constructor state.
                    life = capture.LifeMagnet;
                }
                var body = state.HasMount ? VanillaPlayerMountHitbox1458.Resolve(state.MountType)
                    : (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
                int range = VanillaWorldItemOwner1458.DefaultGrabRange;
                if (mana && drop.ItemNetId is 184 or 1735 or 1868 || drop.ItemNetId == 4143) range += VanillaWorldItemOwner1458.ManaMagnetRange;
                if (life && drop.ItemNetId is 58 or 1734 or 1867) range += VanillaWorldItemOwner1458.LifeMagnetRange;
                if (gold && drop.ItemNetId is >= 71 and <= 74) range += VanillaWorldItemOwner1458.CoinMagnetRange;
                if (treasure) range += VanillaWorldItemOwner1458.TreasureMagnetRange;
                if (drop.ItemNetId == 3822) range += 50;
                if (facts.Nebula) range += 100;
                candidates[index] = new(state.Player.Slot.Value, state.PositionX, state.PositionY, (int)body.Item1,
                    (int)body.Item2, false, true, mana, life, range);
            }
            selected = VanillaWorldItemOwner1458.Select(drop.ItemNetId, drop.PositionX, drop.PositionY, grabDelay, grabPlayer, candidates);
            if (selected == byte.MaxValue) return true;
            byte selectedSlot = selected;
            var candidate = candidates.First(p => p.CanPull && p.Slot == selectedSlot);
            // Wiring.IsHopperInRangeOf excludes pickups that cannot enter an inventory.
            if (facts.NotInventory || VanillaWorldItemOwner1458.InGrabRange(candidate, drop.PositionX, drop.PositionY)) return true;
            if (!TryHasHopper(drop.PositionX, drop.PositionY, out bool hopper)) return false;
            if (hopper)
            {
                var capture = players.First(p => p.State.Player.Slot.Value == selectedSlot);
                if (capture.Appearance is { DifficultyFlags: var flags } && (flags & 8) != 0) return false;
                selected = byte.MaxValue;
            }
            return true;
        }

        private bool TryHasHopper(float x, float y, out bool found)
        {
            found = false;
            if (tiles is null || !float.IsFinite(x) || !float.IsFinite(y)) return false;
            const int halfHopper = 96; // Wiring.HopperGrabHitboxSize / 2.
            int left = Math.Clamp(((int)x - halfHopper) / 16, 0, tiles.Dimensions.WidthTiles - 1);
            int right = Math.Clamp(((int)x + VanillaWorldItemOwner1458.PhysicalWidth + halfHopper) / 16, 0, tiles.Dimensions.WidthTiles - 1);
            int top = Math.Clamp(((int)y - halfHopper) / 16, 0, tiles.Dimensions.HeightTiles - 1);
            int bottom = Math.Clamp(((int)y + VanillaWorldItemOwner1458.PhysicalHeight + halfHopper) / 16, 0, tiles.Dimensions.HeightTiles - 1);
            for (int tx = left; tx <= right; tx++) for (int ty = top; ty <= bottom; ty++)
            {
                if (terrain.Count == RuntimeWorldItemStore.VanillaCapacity * 196) return false; // At most 14x14 cells for each admitted allocation.
                bool hopper = IsHopper(tiles.Get(tx, ty));
                terrain.Add((tx, ty, hopper)); found |= hopper;
            }
            return true;
        }

        private static bool IsHopper(in WorldTile tile) => tile.IsActive && tile.Type is 21 or 467 &&
            (tile.Flags & WorldTileFlagMasks.Wires) != 0;
    }
}

internal static class WorldItemPickupSlotExtensions1458
{
    internal static bool ContainsAnyType(this ReadOnlySpan<WorldItemPickupSlot1458> slots, int type)
    { foreach (var slot in slots) if (slot.Type == type && slot.Stack > 0) return true; return false; }
}
