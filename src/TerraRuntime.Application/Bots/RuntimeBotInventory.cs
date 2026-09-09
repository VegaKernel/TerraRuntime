using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

using static TerraRuntime.Application.Bots.BotPolicy;

namespace TerraRuntime.Application.Bots;

internal sealed class RuntimeBotInventory(
    BotState bot, ServerPlayerAuthority serverPlayers, WorldItemAuthority worldItems,
    RuntimeBotResourceLeases leases, WorldRuntimeIdentity world) : IRuntimeBotInventory
{
    private readonly WorldItemSnapshot[] worldItemBuffer = new WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];

    public RuntimeBotActionResult Pickup(in RuntimeBotObservationSnapshot observation, WorldItemHandle item = default)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        return TryPickupOneUsefulItem(bot, observation.Self, item);
    }

    public RuntimeBotActionResult UseConsumables(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (!serverPlayers.TryGet(bot.Player, out var self) || self.IsDead)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        ExpireBuffs(bot, observation.Tick);
        TryAutoHeal(bot, self, observation.Tick);
        // Healing can change HP; mana must never write the pre-healing snapshot back.
        if (serverPlayers.TryGet(bot.Player, out self)) TryAutoMana(bot, self, observation.Tick);
        return RuntimeBotActionResult.Success;
    }

    public void UseCombatBuffs(in RuntimeBotObservationSnapshot observation)
    {
        if (RuntimeBotObservationScope.IsCurrent(bot, world, observation)) TryAutoUseCombatBuffs(bot, observation.Tick);
    }

    public RuntimeBotInventorySummary CaptureSummary()
    {
        int heal = 0, mana = 0, arrows = 0, bullets = 0;
        bool mirror = false, pick = false;
        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryEndExclusive; slot++)
        {
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out var item) || item.IsEmpty) continue;
            if (item.ItemType == bot.Loadout.ArrowAmmo) arrows += item.Stack;
            if (item.ItemType == bot.Loadout.BulletAmmo) bullets += item.Stack;
            if (item.ItemType == VanillaItemIds.MagicMirror && slot == MirrorSlot && item.Stack == 1) mirror = true;
            if (item.ItemType == VanillaItemIds.VortexPickaxe && slot == 6 && item.Stack == 1) pick = true;
            if (!VanillaBotItemDefinitionCatalog1458.TryGet(item.ItemType, out var definition)) continue;
            if (definition.Kind == VanillaBotItemKind.HealingPotion) heal += item.Stack;
            if (definition.Kind == VanillaBotItemKind.ManaPotion) mana += item.Stack;
        }
        return new(heal, mana, arrows, bullets, mirror, pick);
    }

    public WorldItemSnapshot? FindUsefulItem(in PlayerStateSnapshot self)
    {
        int count = worldItems.CopyActive(worldItemBuffer);
        WorldItemSnapshot? best = null;
        float distance = GuardRadiusSquared;
        for (int i = 0; i < count; i++)
        {
            var item = worldItemBuffer[i];
            if (!TryClassifyUsefulWorldItem(bot, item, out var definition) ||
                !leases.IsAvailable(new(bot.Id, bot.Player, world), RuntimeBotResourceKey.ForItem(world, item.Handle), bot.CurrentTick) ||
                item.OwnerPlayerId != byte.MaxValue && item.OwnerPlayerId != bot.Player.Slot.Value ||
                !TryPlanInventoryDeposit(bot, item, definition, out _, out _, out _)) continue;
            float next = DistanceSquared(self.PositionX, self.PositionY, item.PositionX, item.PositionY);
            if (next < distance) { best = item; distance = next; }
        }
        return best;
    }

    private RuntimeBotActionResult TryPickupOneUsefulItem(BotState bot, in PlayerStateSnapshot self, WorldItemHandle requested)
    {
        int count = worldItems.CopyActive(worldItemBuffer);
        for (int index = 0; index < count; index++)
        {
            WorldItemSnapshot item = worldItemBuffer[index];
            if (requested.IsAssigned && item.Handle != requested) continue;
            if (!TryClassifyUsefulWorldItem(bot, in item, out VanillaBotItemDefinition1458 definition) ||
                !IsTrustedPickupEligible(bot, in self, in item, in definition) ||
                !TryPlanInventoryDeposit(bot, in item, in definition, out short slot, out ServerPlayerItemState before,
                    out ServerPlayerItemState after))
            {
                continue;
            }

            if (!worldItems.TryCapture(item.Handle.Slot, out WorldItemSnapshot current) || current.Handle != item.Handle)
                continue;
            var owner = new RuntimeBotLeaseOwner(bot.Id, bot.Player, world);
            var resource = RuntimeBotResourceKey.ForItem(world, item.Handle);
            if (!leases.TryAcquire(owner, resource, tick: bot.CurrentTick)) continue;
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in after))
            {
                leases.Release(owner, resource);
                continue;
            }
            bool taken = worldItems.TryTakeTrusted(item.Handle, out _);
            leases.Release(owner, resource);
            if (taken)
            {
                bot.LastPickedItem = item.Handle;
                return RuntimeBotActionResult.Success;
            }
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in before))
                throw new InvalidOperationException("Bot inventory rollback failed after stale world-item pickup.");
        }
        return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.ItemUnavailable);
    }

    private bool TryClassifyUsefulWorldItem(
        BotState bot,
        in WorldItemSnapshot item,
        out VanillaBotItemDefinition1458 definition)
    {
        definition = default;
        if (!item.IsActive || item.Prefix != 0 || !item.TryGetItemType(out ItemTypeId itemType) ||
            !VanillaBotItemDefinitionCatalog1458.TryGet(itemType, out definition))
        {
            return false;
        }

        return definition.Kind switch
        {
            VanillaBotItemKind.MiningMaterial => bot.Configuration.Mode == RuntimeBotMode.Mining &&
                (itemType == VanillaItemIds.DirtBlock || itemType == VanillaItemIds.StoneBlock ||
                 VanillaTileDefinitionCatalog.Get(new TileTypeId((int)bot.Configuration.MiningOre)).DropRule.PrimaryItem == itemType),
            VanillaBotItemKind.RequiredAmmo =>
                IsRequiredAmmo(bot, itemType),
            VanillaBotItemKind.HealingPotion => true,
            VanillaBotItemKind.ManaPotion => true,
            VanillaBotItemKind.UsefulBuffPotion =>
                VanillaBotItemDefinitionCatalog1458.IsSupportedCombatBuff(definition.BuffType) &&
                IsBuffUsefulForWeapon(bot.Configuration.WeaponPolicy, definition.BuffType),
            _ => false
        };
    }

    private static bool IsTrustedPickupEligible(
        BotState bot,
        in PlayerStateSnapshot self,
        in WorldItemSnapshot item,
        in VanillaBotItemDefinition1458 definition)
    {
        byte botSlot = bot.Player.Slot.Value;
        if (item.ShimmerTime != 0f ||
            item.OwnerPlayerId != byte.MaxValue && item.OwnerPlayerId != botSlot ||
            item.GrabDelayTime > 0 && (item.GrabDelayPlayer == botSlot || item.GrabDelayPlayer == byte.MaxValue) ||
            item.Shimmered && MathF.Sqrt(item.VelocityX * item.VelocityX + item.VelocityY * item.VelocityY) >= 0.2f)
        {
            return false;
        }

        return RectanglesIntersect(
            self.PositionX,
            self.PositionY,
            PlayerAuthority.VanillaBasePlayerWidth,
            PlayerAuthority.VanillaBasePlayerHeight,
            item.PositionX,
            item.PositionY,
            definition.Width,
            definition.Height);
    }

    private bool TryPlanInventoryDeposit(
        BotState bot,
        in WorldItemSnapshot worldItem,
        in VanillaBotItemDefinition1458 definition,
        out short slot,
        out ServerPlayerItemState before,
        out ServerPlayerItemState after)
    {
        slot = -1;
        before = default;
        after = default;
        if (!worldItem.TryGetItemType(out ItemTypeId itemType) || worldItem.Stack <= 0)
            return false;

        Span<short> candidateSlots = stackalloc short[VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount - 1];
        int candidateCount = BuildStorageSlotOrder(definition.Kind, candidateSlots);
        short firstEmpty = -1;
        ServerPlayerItemState firstEmptyState = default;
        for (int i = 0; i < candidateCount; i++)
        {
            short candidate = candidateSlots[i];
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, candidate, out ServerPlayerItemState state))
                return false;
            if (state.IsEmpty)
            {
                if (firstEmpty < 0)
                {
                    firstEmpty = candidate;
                    firstEmptyState = state;
                }
                continue;
            }
            if (state.ItemType != itemType || state.Prefix != VanillaPrefixIds.None ||
                state.Stack + worldItem.Stack > VanillaBotItemDefinitionCatalog1458.CommonMaxStack)
            {
                continue;
            }

            slot = candidate;
            before = state;
            after = state with { Stack = checked((short)(state.Stack + worldItem.Stack)) };
            return true;
        }

        if (firstEmpty < 0)
            return false;

        slot = firstEmpty;
        before = firstEmptyState;
        after = new ServerPlayerItemState(
            firstEmpty,
            itemType,
            worldItem.Stack,
            VanillaPrefixIds.None,
            0);
        return true;
    }

    private static int BuildStorageSlotOrder(VanillaBotItemKind kind, Span<short> destination)
    {
        int count = 0;
        if (kind == VanillaBotItemKind.RequiredAmmo)
        {
            for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
                 slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
                 slot++)
            {
                destination[count++] = slot;
            }
        }

        // Slot 0 is the bot's held weapon. QuickHeal/QuickBuff source scans the same ordinary inventory span.
        for (short slot = 1; slot < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive; slot++)
            destination[count++] = slot;
        return count;
    }

    private void TryAutoHeal(BotState bot, in PlayerStateSnapshot self, long tick)
    {
        if (!self.HasHealth || self.Life <= 0 || self.Life >= self.MaxLife || tick < bot.PotionDelayUntilTick)
            return;

        int missingLife = self.MaxLife - self.Life;
        int bestDifference = -self.MaxLife;
        short bestSlot = -1;
        ServerPlayerItemState bestItem = default;
        VanillaBotItemDefinition1458 bestDefinition = default;
        for (short slot = VanillaPlayerItemSlotCatalog.InventoryStart;
             slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out ServerPlayerItemState item) ||
                item.IsEmpty || item.Prefix != VanillaPrefixIds.None ||
                !VanillaBotItemDefinitionCatalog1458.TryGet(item.ItemType, out VanillaBotItemDefinition1458 definition) ||
                definition.Kind != VanillaBotItemKind.HealingPotion ||
                !VanillaBotItemDefinitionCatalog1458.IsBetterQuickHealCandidate(
                    missingLife, in definition, bestDifference, out int candidateDifference))
            {
                continue;
            }

            bestDifference = candidateDifference;
            bestSlot = slot;
            bestItem = item;
            bestDefinition = definition;
        }

        // Consumption timing is bot policy, not an alteration of QuickHeal's source candidate ordering.
        // Save a large potion for efficient healing unless health is already at or below half.
        if (bestSlot < 0 || (missingLife < bestDefinition.HealLife && self.Life > self.MaxLife / 2))
            return;

        ServerPlayerItemState consumed = bestItem.Stack == 1
            ? new ServerPlayerItemState(bestSlot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : bestItem with { Stack = checked((short)(bestItem.Stack - 1)) };
        if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
            return;

        short healedLife = checked((short)Math.Min(self.MaxLife, self.Life + bestDefinition.HealLife));
        var vitals = new ServerPlayerVitalsState(healedLife, self.MaxLife, self.Mana, self.MaxMana);
        if (!serverPlayers.SetVitals(bot.ServerPlayerId, in vitals))
        {
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in bestItem))
                throw new InvalidOperationException("Bot healing-item rollback failed after vitals rejection.");
            return;
        }

        bot.PotionDelayUntilTick = tick + VanillaBotItemDefinitionCatalog1458.OrdinaryHealingPotionDelayTicks;
    }

    private void TryAutoMana(BotState bot, in PlayerStateSnapshot self, long tick)
    {
        _ = tick;
        if (!self.HasMana || self.Mana < 0 || self.Mana >= self.MaxMana || self.IsDead)
            return;

        // Player.QuickMana_GetItemToUse scans inventory[0..57] in order. In the pinned build ordinary mana
        // potions are consumables with healMana but not Item.potion, so healing Potion Sickness does not block them.
        for (short slot = VanillaPlayerItemSlotCatalog.InventoryStart;
             slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out ServerPlayerItemState item) ||
                item.IsEmpty || item.Prefix != VanillaPrefixIds.None ||
                !VanillaBotItemDefinitionCatalog1458.TryGet(item.ItemType, out VanillaBotItemDefinition1458 definition) ||
                definition.Kind != VanillaBotItemKind.ManaPotion || definition.HealMana <= 0)
            {
                continue;
            }

            if (self.MaxMana - self.Mana < definition.HealMana && self.Mana > self.MaxMana / 5)
                return;

            ServerPlayerItemState consumed = item.Stack == 1
                ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
                : item with { Stack = checked((short)(item.Stack - 1)) };
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
                return;

            short restoredMana = checked((short)Math.Min(self.MaxMana, self.Mana + definition.HealMana));
            var vitals = new ServerPlayerVitalsState(self.Life, self.MaxLife, restoredMana, self.MaxMana);
            if (!serverPlayers.SetVitals(bot.ServerPlayerId, in vitals))
            {
                if (!serverPlayers.SetItem(bot.ServerPlayerId, in item))
                    throw new InvalidOperationException("Bot mana-item rollback failed after vitals rejection.");
            }
            return;
        }
    }

    private void TryAutoUseCombatBuffs(BotState bot, long tick)
    {
        for (short slot = VanillaPlayerItemSlotCatalog.InventoryStart;
             slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out ServerPlayerItemState item) ||
                item.IsEmpty || item.Prefix != VanillaPrefixIds.None ||
                !VanillaBotItemDefinitionCatalog1458.TryGet(item.ItemType, out VanillaBotItemDefinition1458 definition) ||
                definition.Kind != VanillaBotItemKind.UsefulBuffPotion ||
                !VanillaBotItemDefinitionCatalog1458.IsSupportedCombatBuff(definition.BuffType) ||
                !IsBuffUsefulForWeapon(bot.Configuration.WeaponPolicy, definition.BuffType) ||
                IsBuffActive(bot, definition.BuffType, tick))
            {
                continue;
            }

            ServerPlayerItemState consumed = item.Stack == 1
                ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
                : item with { Stack = checked((short)(item.Stack - 1)) };
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
                continue;

            // TerrariaServer 1.4.5.8 MessageBuffer case 55 is targeted PvP-buff delivery: a client
            // applies it only when the encoded player slot is Main.myPlayer. A server-owned fake player
            // therefore keeps supported combat-buff state inside the trusted simulation instead of
            // broadcasting packet 55 as if it were remote-player buff replication.
            bot.ActiveBuffs[definition.BuffType] = tick + definition.BuffTimeTicks;
        }
    }

    internal bool TryBuildBotCombatSnapshot(BotState bot, long tick, out VanillaPlayerCombatSnapshot snapshot)
    {
        if (!serverPlayers.TryCaptureCombatSnapshot(bot.Player, out snapshot))
            return false;
        if (IsBuffActive(bot, VanillaBuffIds.Archery, tick))
            snapshot = snapshot with { ArrowDamage = snapshot.ArrowDamage * 1.1f };
        if (IsBuffActive(bot, VanillaBuffIds.Wrath, tick))
            snapshot = snapshot with
            {
                MeleeDamage = snapshot.MeleeDamage + 0.1f,
                RangedDamage = snapshot.RangedDamage + 0.1f
            };
        return true;
    }

    internal static bool TryFindAmmoSlot(
        ServerPlayerAuthority serverPlayers,
        ServerPlayerId id,
        ItemTypeId requiredAmmo,
        out short slot,
        out ServerPlayerItemState item)
    {
        for (short candidate = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
             candidate < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
             candidate++)
        {
            if (serverPlayers.TryGetItem(id, candidate, out item) &&
                !item.IsEmpty && item.ItemType == requiredAmmo && item.Prefix == VanillaPrefixIds.None)
            {
                slot = candidate;
                return true;
            }
        }
        for (short candidate = VanillaPlayerItemSlotCatalog.MainInventoryStart;
             candidate < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive;
             candidate++)
        {
            if (serverPlayers.TryGetItem(id, candidate, out item) &&
                !item.IsEmpty && item.ItemType == requiredAmmo && item.Prefix == VanillaPrefixIds.None)
            {
                slot = candidate;
                return true;
            }
        }

        slot = -1;
        item = default;
        return false;
    }

}
