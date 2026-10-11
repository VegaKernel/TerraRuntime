using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // A bounded ordinary remote profile, not the complete Player.Update scheduler. All players
    // share Main.SwapRandom("UpdatePlayers"); skipping an unsupported earlier actor is unsound.
    internal bool TryTickRemotePlayerPhase()
    {
        Span<PlayerStateSnapshot> bots = stackalloc PlayerStateSnapshot[MaxPlayerSlots];
        if (serverPlayers?.CopySnapshots(bots) > 0) return false;
        // An empty update does not read world/profile facts or consume the player stream.
        // Keep the normal no-player tick allocation-free without reviving retired custody.
        if (membership.Count == 0) return true;
        // Main.UpdateWorld_Players never updates its dedicated local slot255.
        var members = membership.Members.Where(static member => member.Slot.Value < byte.MaxValue)
            .OrderBy(static member => member.Slot.Value).ToArray();
        if (members.Length == 0) return playerUpdateRandomOwned && playerUpdateRandom is not null;
        if (!playerUpdateRandomOwned || playerUpdateRandom is not { } random || playerUpdateWorld is not { } world ||
            worldTiles is not { } tiles || !world.IsValid || membership.Serial == ulong.MaxValue ||
            inventory.Serial == ulong.MaxValue || tiles.Dimensions.SectionCount > 4_096)
            return false;
        ulong memberSerial = membership.Serial;
        ulong inventorySerial = inventory.Serial;
        var beforeRandom = random.Clone();
        var plannedRandom = random.Clone();
        var versions = new long[tiles.Dimensions.SectionCount];
        for (int i = 0; i < versions.Length; i++)
        {
            versions[i] = tiles.GetSectionVersion(TerrariaSectionGeometry.FromLinearIndex(tiles.Dimensions, i));
            if (versions[i] == long.MaxValue || (versions[i] & 1) != 0) return false;
        }

        var healthWorldSource = npcHealthWorldFacts;
        var grapplingSource = npcHealthGrapplingFacts;
        var ownedHealthWorld = remotePlayerHealthWorld;
        var ownedProjectiles = remotePlayerProjectiles;
        bool equipmentExpert = combatEquipmentExpertMode;
        bool equipmentMaster = combatEquipmentMasterMode;
        var localVanity = combatEquipmentLocalVanityArmor;
        var plans = new RemoteItemPhasePlan1458[members.Length];
        // Capture the entire census before the health world/grapple providers can run.
        for (int i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!transferProfiles.TryCapture(member.Connection, out var appearance, out var equipment, out var buffs) ||
                buffs is null || transferProfiles.CaptureBuffState(member.Connection) is not { } buffState)
                return false;
            plans[i] = new(member, member.CaptureSnapshot(), member.ItemPhase, member.PhysicsPhase,
                appearance, equipment, buffs, buffState, member.GodMode, member.OwnsManaMaximum);
            membership.TryGetTownShopSession(member.Connection.Player, out var shopSession);
            plans[i].BeforeShopSession = shopSession;
            if (!inventory.TryCopyInventory(member.Connection, plans[i].InventoryBefore)) return false;
        }
        foreach (var plan in plans)
        {
            bool outside = VanillaPlayerHealthContext1458.IsRemoteOutOfRange(plan.X, plan.Y, 20, 42,
                world.MaxTilesX, world.MaxTilesY);
            bool ghost = (plan.Before.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0;
            if (outside || ghost || plan.Before.IsDead) continue;
            if (ownedHealthWorld is not { } healthWorld || ownedProjectiles is null) return false;
            plan.HealthWorld = healthWorld;
            plan.Grappling = CaptureRemotePlayerGrappling(plan.Before.Player);
            // The ordinary physics owner has no retained hook attachment/movement phase.
            if (plan.Grappling != false ||
                (healthWorldSource is not null && healthWorldSource() != healthWorld) ||
                (grapplingSource is not null && grapplingSource(plan.Before.Player) != plan.Grappling))
                return false;
        }
        var physics = new VanillaServerPlayerDryPhysicsStepper(tiles);
        foreach (var plan in plans)
        {
            if (!TryPlanRemoteItemPhase(plan, in world, physics, plannedRandom)) return false;
        }

        // Finish every external read before the final owned-store comparisons. In particular a
        // later player's provider can mutate an earlier player's indexed buff durations.
        foreach (var plan in plans)
            if (plan.HealthWorld is not null && grapplingSource is not null &&
                grapplingSource(plan.Before.Player) != plan.Grappling)
                return false;
        foreach (var plan in plans)
            if (plan.HealthWorld is { } healthWorld && healthWorldSource is not null && healthWorldSource() != healthWorld)
                return false;

        foreach (var plan in plans)
            if (plan.HealthWorld is not null && CaptureRemotePlayerGrappling(plan.Before.Player) != plan.Grappling)
                return false;

        // No external readers occur after this tail. Adoption cannot expose a partial census.
        if (membership.Serial != memberSerial || inventory.Serial != inventorySerial ||
            !playerUpdateRandomOwned || !ReferenceEquals(playerUpdateRandom, random) ||
            !ReferenceEquals(npcHealthWorldFacts, healthWorldSource) ||
            !ReferenceEquals(npcHealthGrapplingFacts, grapplingSource) ||
            remotePlayerHealthWorld != ownedHealthWorld || !ReferenceEquals(remotePlayerProjectiles, ownedProjectiles) ||
            !random.HasSameState(beforeRandom) || playerUpdateWorld != world ||
            combatEquipmentExpertMode != equipmentExpert || combatEquipmentMasterMode != equipmentMaster ||
            combatEquipmentLocalVanityArmor != localVanity ||
            serverPlayers?.CopySnapshots(bots) > 0) return false;
        for (int i = 0; i < versions.Length; i++)
            if (tiles.GetSectionVersion(TerrariaSectionGeometry.FromLinearIndex(tiles.Dimensions, i)) != versions[i])
                return false;
        foreach (var plan in plans)
        {
            var member = plan.Member;
            membership.TryGetTownShopSession(member.Connection.Player, out var shopSession);
            if (!ReferenceEquals(shopSession, plan.BeforeShopSession) ||
                !membership.TryGet(member.Connection, out var current) || !ReferenceEquals(current, member) ||
                member.CaptureSnapshot() != plan.Before || member.ItemPhase != plan.BeforeItem ||
                member.PhysicsPhase != plan.BeforePhysics || member.GodMode != plan.GodMode ||
                member.OwnsManaMaximum != plan.BeforeOwnsManaMaximum ||
                member.Revision == ulong.MaxValue ||
                !transferProfiles.TryCapture(member.Connection, out var appearance, out var equipment, out var buffs) ||
                appearance != plan.Appearance || !equipment.AsSpan().SequenceEqual(plan.Equipment) ||
                buffs is null || !buffs.AsSpan().SequenceEqual(plan.BuffTypes) ||
                !transferProfiles.IsCurrentBuffState(member.Connection, plan.BeforeBuffs)) return false;
        }
        foreach (var plan in plans)
            if (plan.PublishBuffs) plan.PublishedBuffTypes = plan.Buffs.CaptureTypes();
        var phaseMutations = plans.SelectMany(static plan => plan.InventoryMutations).ToArray();
        if (!inventory.TryPrepareRemotePhase(phaseMutations, inventorySerial, out var inventoryPlan) ||
            !inventoryPlan!.TryAdoptUnpublished()) return false;
        foreach (var plan in plans)
        {
            if (!plan.Changed) continue;
            var member = plan.Member;
            // Every condition was checked above; these owned assignments invoke no callbacks.
            transferProfiles.TryAdoptBuffState(member.Connection, plan.BeforeBuffs, plan.Buffs);
            member.TryAdvanceRemotePhaseRevision();
            member.ItemPhase = plan.Item;
            if (plan.InventoryMutations.Any(static mutation => mutation.CursorSourceDebit is not null) &&
                member.CursorMove?.Inventory is { } consumed &&
                plan.InventoryMutations.Any(mutation=>ReferenceEquals(mutation.CursorSourceDebit,consumed)))
                member.CursorMove = null;
            ObserveShopPricingPhase(member);
            if (plan.Item?.MagicPayment is not null) member.OwnsManaMaximum = true;
            member.PhysicsPhase = plan.Physics;
            member.ItemAnimation = plan.Item!.Value.Selected.Animation;
            member.ItemRotation = plan.Rotation;
            member.NpcHealth = plan.Health;
            member.NpcLifeCurrent = plan.Health is not null;
            member.DerivedLifeMax = plan.Maximum;
            member.Debuffs = plan.Debuffs;
            member.Mana = checked((short)plan.Item.Value.Mana.Mana);
            if (plan.PublishMana) member.MaxMana = checked((short)plan.Item.Value.BaseManaMaximum);
            member.PositionX = plan.X;
            member.PositionY = plan.Y;
            member.VelocityX = plan.Vx;
            member.VelocityY = plan.Vy;
            member.MarkRemotePhaseSnapshot();
        }
        random.CopyStateFrom(plannedRandom);
        Exception? first = null;
        int mutationIndex = 0;
        for (int index = 0; index < plans.Length; index++)
        {
            var plan = plans[index];
            foreach (var mutation in plan.InventoryMutations)
            {
                int journalIndex = mutationIndex++;
                if (membership.TryGet(mutation.Connection, out var current) && ReferenceEquals(current, plan.Member) &&
                    inventoryPlan.IsAcceptedItemCurrent(journalIndex))
                {
                    try
                    {
                        var report = mutation.After.ToCommitRequest(mutation.Connection.Player.Slot, mutation.Slot);
                        events?.PlayerEquipmentUpdated(mutation.Connection, in report);
                    }
                    catch (Exception error) { first ??= error; }
                }
            }
            if (plan.PublishHealth && membership.TryGet(plan.Member.Connection, out var healthMember) &&
                ReferenceEquals(healthMember, plan.Member) && healthMember.NpcHealth == plan.Health &&
                healthMember.ItemPhase == plan.Item)
            {
                try
                {
                    var report = new PlayerHealthCommitRequest(plan.Member.Slot,
                        checked((short)plan.Health!.Value.Life), healthMember.MaxLife);
                    events?.PlayerHealthUpdated(plan.Member.Connection, in report);
                }
                catch (Exception error) { first ??= error; }
            }
            if (plan.PublishMana && membership.TryGet(plan.Member.Connection, out var manaMember) &&
                ReferenceEquals(manaMember, plan.Member) && manaMember.Mana == plan.Item!.Value.Mana.Mana &&
                manaMember.MaxMana == plan.Item.Value.BaseManaMaximum && manaMember.ItemPhase?.BaseManaMaximum == plan.Item.Value.BaseManaMaximum)
            {
                try
                {
                    var report = new PlayerManaCommitRequest(plan.Member.Slot, manaMember.Mana, manaMember.MaxMana);
                    events?.PlayerManaUpdated(plan.Member.Connection, in report);
                }
                catch (Exception error) { first ??= error; }
            }
            if (plan.PublishedBuffTypes is { } publishedBuffs &&
                membership.TryGet(plan.Member.Connection, out var buffMember) && ReferenceEquals(buffMember, plan.Member) &&
                transferProfiles.IsCurrentBuffState(plan.Member.Connection, plan.Buffs))
            {
                try
                {
                    var report = new PlayerBuffTypesCommitRequest(plan.Member.Slot, publishedBuffs);
                    events?.PlayerBuffTypesUpdated(plan.Member.Connection, in report);
                }
                catch (Exception error) { first ??= error; }
            }
        }
        if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
        return true;
    }

    private bool TryPlanRemoteItemPhase(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerUpdateWorld1458 world, VanillaServerPlayerDryPhysicsStepper physics,
        VanillaUnifiedRandom1458 random)
    {
        var member = plan.Member;
        if (plan.Item is not { } item || (item.ShopDiscountStates & ~15) != 0 || item.ClientUiPriceArithmeticMask > 3 || plan.Physics is not { } physical ||
            item.DeadTime is < 0 or >= int.MaxValue || item.RespawnTimer < 0 || item.ManaPotionDelay < 0 ||
            member.NpcHealth is not { SourceProfileKnown: true } || member.HasMount || plan.GodMode ||
            (plan.Appearance?.ConsumableUnlockFlags ?? 0) != 0 ||
            !TryCaptureProvenRemoteEquipment(plan.Equipment, plan.Appearance, out var equipmentCombat, out int manaMaximumBonus) ||
            plan.BuffTypes.Any(static type => type.Value is not (5 or 16 or 21 or 23 or 93 or 94 or 112 or 114 or 115 or 117 or 321)) ||
            member.MiscFlags1 != 0 || (member.MiscFlags2 & ~((1 << 6) | (1 << 7))) != 0 ||
            (member.ControlFlags & ~0x7c) != 0 ||
            (member.MovementFlags & ~0x54) != 0 || physical.GravityDirection is not (1f or -1f) ||
            item.Selected.Animation != member.ItemAnimation) return false;
        // Packet42 and accepted projectile mana debits write the owned current value. Neither
        // update resets the retained source regeneration count or delay.
        item = item with { Mana = item.Mana with { Mana = member.Mana } };

        bool outside = VanillaPlayerHealthContext1458.IsRemoteOutOfRange(plan.X, plan.Y, 20, 42,
            world.MaxTilesX, world.MaxTilesY);
        bool ghost = (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0;
        plan.Maximum = VanillaPlayerHealthContext1458.Resolve(member.DerivedLifeMax, member.BaseLifeMax,
            0, outside, ghost, member.IsDead);
        plan.Debuffs = VanillaPlayerHealthContext1458.ResolveDebuffs(member.Debuffs,
            plan.Buffs.CaptureDebuffFlags(), outside, ghost, member.IsDead);
        int delay = item.Selected.PotionDelay;
        if (outside)
        {
            if (item.MagicPayment is { } payment)
                item = item with { MagicPayment = payment with { EligibleBirth = false, AwaitingPhase = false } };
            bool combatBuff = plan.BuffTypes.Any(static type => type.Value is 5 or 16 or 114 or 115 or 117 or 321);
            if (combatBuff)
            {
                if (item.DerivedCombat is not { } retained || !IsValidDerivedCombat(in retained) ||
                    !VanillaPlayerCombatBuffs1458.TryApply(in retained, plan.BuffTypes, out var compounded) ||
                    !IsValidDerivedCombat(in compounded)) return false;
                item = item with { DerivedCombat = compounded };
            }
            int duration = plan.Buffs.GetLastActiveDuration(new BuffTypeId(21));
            if (duration > 0) delay = duration;
            plan.Item = item with { Selected = item.Selected with { Animation = 0, PotionDelay = delay } };
            plan.Changed = true;
            return true;
        }
        if (!VanillaSelectedConsumable1458.TryStepHeat(item.ManaHeat, random.NextDouble, random.Next, out float heat))
            return false;
        if (item.ToolTime is not >= 0 ||
            !VanillaRemoteMeleeItemCheck1458.TryStepAttackCooldown(item.AttackCD, item.Selected.Animation, out int attackCD))
            return false;
        delay = Math.Max(0, delay - 1);
        item = item with
        {
            ManaHeat = heat,
            AttackCD = attackCD,
            ManaPotionDelay = Math.Max(0, item.ManaPotionDelay - 1),
            Selected = item.Selected with { PotionDelay = delay }
        };
        if (ghost || member.IsDead)
        {
            if (item.MagicPayment is { } payment)
                item = item with { MagicPayment = payment with { EligibleBirth = false, AwaitingPhase = false } };
            // Stationary ghost/dead profiles with retained nonconstructor clocks are independently captured.
            // Moving ghosts and respawn side effects require their own retained state.
            if (ghost && (plan.Vx != 0f || plan.Vy != 0f || (member.ControlFlags & 0x1c) != 0)) return false;
            // Dedicated UpdateDead can turn a hardcore player into a ghost when the respawn
            // clock expires. This slice does not own surviveHardcoreDeath or the ghost transition.
            if (!ghost && plan.Appearance is { DifficultyFlags: var difficulty } &&
                (difficulty & 2) != 0 && (difficulty & 8) == 0) return false;
            if (member.IsDead && !ghost)
            {
                plan.Buffs.ClearNonPersistentOnDeath();
                plan.Health = member.NpcHealth!.Value with { Life = 0 };
                item = item with
                {
                    ManaHeat = 1f,
                    Channel = false,
                    ManaPotionDelay = 0,
                    DeadTime = item.DeadTime + 1,
                    RespawnTimer = Math.Clamp(item.RespawnTimer - 1, 0, 3_600),
                    Selected = item.Selected with { Animation = 0, PotionDelay = 0 }
                };
                plan.Physics = physical with { GravityDirection = 1f };
            }
            if (ghost)
                // Source Ghost() has its own crit reset; dead/outside retain their prior fields.
                item = item with { Mana = item.Mana with { Maximum = item.BaseManaMaximum },
                    DerivedCombat = VanillaPlayerCombatSnapshot.Baseline };
            plan.Item = item;
            plan.Changed = true;
            return true;
        }
        if (!inventory.TryGet(member.Connection, member.SelectedItem, out var selected)) return false;
        if (item.SelectedPotionUse is { Completed:false } provisional && provisional.Slot == member.SelectedItem &&
            provisional.Player == member.Connection.Player && member.CursorMove?.Inventory is { } potionDebit &&
            potionDebit.TryCaptureSelectedPotionDebit(inventory,member.Connection,out var originalPotion,out _) &&
            originalPotion == provisional.Before) selected=originalPotion;
        if (!selected.IsCanonical ||
            (!(selected.Prefix == VanillaPrefixIds.None &&
                VanillaSelectedConsumableCatalog1458.TryGet(selected.ItemType, out _)) &&
                !VanillaRemoteRangedItemCheck1458.IsSupported(selected.ItemType, selected.Prefix,
                    world.WindowsItemPrefixArithmetic) &&
                !VanillaRemoteMeleeItemCheck1458.IsSupported(selected.ItemType, selected.Prefix,
                    world.WindowsItemPrefixArithmetic) &&
                !VanillaRemotePassiveItemCheck1458.IsSupported(selected.ItemType, selected.Prefix) &&
                !VanillaRemoteChannelledMagic1458.IsSupported(selected.ItemType, selected.Prefix, world.WindowsItemPrefixArithmetic))) return false;
        if (VanillaRemoteChannelledMagic1458.IsSupported(selected.ItemType, selected.Prefix, world.WindowsItemPrefixArithmetic) &&
            (item.BaseManaMaximum <= 0 || item.MagicPayment is { } malformedPayment && !malformedPayment.IsValid)) return false;
        var critArithmetic = world.WindowsItemPrefixArithmetic
            ? VanillaBulletSourceArithmetic1458.WindowsClr4X86 : VanillaBulletSourceArithmetic1458.CoreClrSingle;
        if (!VanillaSelectedItemCrit1458.TryResolve(selected.ItemType, selected.Prefix, critArithmetic, out int itemCrit))
            return false;
        // ResetEffects derives all three fields from this phase's selected Item.crit.
        // Later selection/equipment reports must not reconstruct these retained values.
        // UpdateBuffs precedes the final magic-damage heat/sickness writer; ItemCheck's
        // new mana-potion buff is offered afterward and first affects the following phase.
        int sicknessDuration = plan.Buffs.GetLastActiveDuration(new BuffTypeId(94));
        if (!VanillaPlayerCombatBuffs1458.TryApply(in equipmentCombat, plan.BuffTypes, out var buffCombat) ||
            !VanillaPlayerCombatBuffs1458.TryResolveMagicDamage(buffCombat.MagicDamage, sicknessDuration, heat,
                world.WindowsItemPrefixArithmetic, out float magicDamage)) return false;
        var derivedCombat = buffCombat with
        {
            MeleeCrit = buffCombat.MeleeCrit + itemCrit,
            RangedCrit = buffCombat.RangedCrit + itemCrit,
            MagicCrit = buffCombat.MagicCrit + itemCrit,
            MagicDamage = magicDamage
        };
        if (!IsValidDerivedCombat(in derivedCombat)) return false;
        // UpdateEquips precedes this writer in original Player.Update. An open
        // shop retains the prior availability even when functional gear changes.
        item = (item with { DerivedCombat = derivedCombat }).AdvanceShopDiscount(derivedCombat.DiscountEquipped);
        if (plan.Maximum is not { } maximum || plan.HealthWorld is not { } capturedHealthWorld ||
            !TryPlanNpcHealth(member, maximum, false, false, out var health, selectedItemPhase: true,
                selectedHealthInputs: (capturedHealthWorld, plan.Grappling)) ||
            health is not { SourceProfileKnown: true } knownHealth) return false;

        int potionDuration = plan.Buffs.GetLastActiveDuration(new BuffTypeId(21));
        if (potionDuration > 0) delay = potionDuration;
        var manaFacts = new PlayerManaRegenerationFacts1458(plan.Vx, plan.Vy, plan.Grappling!.Value, false, false, 0, 0f, 0);
        if (!VanillaRemoteManaRegeneration1458.TryStep(item.Mana with { Maximum = (int)Math.Min((long)item.BaseManaMaximum + manaMaximumBonus, 400) },
            in manaFacts, out var mana, out _)) return false;
        mana = mana with { Mana = Math.Min(mana.Mana, mana.Maximum), Count = Math.Max(0, mana.Count) };

        bool left = (member.ControlFlags & 4) != 0;
        bool right = (member.ControlFlags & 8) != 0;
        var horizontal = ResolveRemoteHorizontalIntent(left, right, plan.Vx);
        var jump = (member.ControlFlags & 16) != 0 ? ServerPlayerJumpIntent.Held : ServerPlayerJumpIntent.Released;
        var source = plan.Before;
        var previousJump = physical.Jump;
        var contacts = physical.Contacts;
        if (!physics.TryStep(in source, horizontal, jump, in previousJump, in contacts,
            in world, out var motion, out var nextJump) || motion.LiquidContacts != default)
            return false;
        plan.X = motion.PositionX;
        plan.Y = motion.PositionY;
        plan.Vx = motion.VelocityX;
        plan.Vy = motion.VelocityY;
        // Player.Update (1.4.5.8) clears wingTime when wingsLogic is zero; the generic
        // jump helper's grounded recharge belongs to its separate wing-capable callers.
        plan.Physics = new(nextJump with { WingTime = 0 }, motion.LiquidContacts);
        var facts = new PlayerSelectedConsumableFacts1458(selected.ItemType, knownHealth.Life, maximum,
            mana.Mana, mana.Maximum, (member.ControlFlags & 32) != 0, (member.MiscFlags2 & 64) != 0,
            plan.Buffs.CountActive(new BuffTypeId(23)) != 0, false, true, false);
        var clocks = item.Selected with { PotionDelay = delay };
        PlayerSelectedConsumableTransition1458 use;
        bool beganActualUse;
        bool mining = false;
        if (VanillaManaCrystalUse1458.IsSupported(selected.ItemType, selected.Prefix))
        {
            if (!TryPlanManaCrystal(plan, selected, item, clocks, in facts, random, out use, out item)) return false;
            if (plan.PublishMana) mana = mana with { Maximum = item.Mana.Maximum };
            beganActualUse = use.BeganUse;
        }
        else if (VanillaRemoteChannelledMagic1458.IsSupported(selected.ItemType, selected.Prefix, world.WindowsItemPrefixArithmetic))
        {
            var priorPayment = item.MagicPayment;
            bool prepaid = priorPayment is { AwaitingPhase: true } paid &&
                paid.Matches(member.Connection.Player, member.SelectedItem, selected, item.BaseManaMaximum);
            RuntimePlayerMagicRefill1458? refill = null;
            int available = mana.Mana;
            bool attempt = facts.ControlUseItem && clocks.ReleaseUseItem && clocks.Animation == 0 &&
                !facts.Cursed && !facts.CrowdControlled && !facts.SelectionBuffered;
            if (!VanillaChanneledMagicWeaponStats1458.TryResolve(selected.ItemType, selected.Prefix, world.WindowsItemPrefixArithmetic, out var magicWeapon, out _) ||
                item.DerivedCombat is not { } magicCombat ||
                !VanillaRemoteChannelledMagic1458.TryResolveManaCost(magicWeapon.ManaCost, magicCombat.ManaCostMultiplier, out int cost))
                return false;
            if (attempt && !prepaid && magicCombat.ManaFlower && available < cost)
            {
                if (priorPayment is { Attempt: ulong.MaxValue } ||
                    !TryStageAutoQuickMana(member.Connection, (priorPayment?.Attempt ?? 0) + 1,
                        available, mana.Maximum, item.ManaPotionDelay, plan.InventoryBefore, plan.Buffs,
                        out available, out refill)) return false;
                if (refill is { } restored)
                {
                    plan.InventoryMutations.Add(new(member.Connection, restored.Slot, restored.Before, restored.After));
                    plan.PublishMana = true;
                    plan.PublishBuffs = true;
                }
            }
            var magicFacts = new PlayerRemoteChannelledMagicFacts1458(selected.ItemType, selected.Prefix,
                available, item.Channel, item.SlowMagicUse, facts.ControlUseItem, facts.LastUseSuccess,
                facts.Cursed, facts.CrowdControlled, facts.SelectionBuffered, prepaid,
                prepaid && priorPayment!.Value.SlowMagicUse, magicCombat.ManaCostMultiplier, world.WindowsItemPrefixArithmetic);
            if (!VanillaRemoteChannelledMagic1458.TryStep(clocks, in magicFacts, random.Next, out var magicUse))
                return false;
            if (magicUse.AttemptedUse)
            {
                if (prepaid) item = item with { MagicPayment = priorPayment!.Value with { AwaitingPhase = false } };
                else
                {
                    if (priorPayment is { Attempt: ulong.MaxValue }) return false;
                    item = item with { MagicPayment = new(member.Connection.Player,
                        (priorPayment?.Attempt ?? 0) + 1, member.SelectedItem, selected,
                        item.BaseManaMaximum, magicUse.Mana, magicUse.SlowMagicUse,
                        magicUse.BeganActualUse, false, false) { Refill = refill } };
                }
            }
            else if (priorPayment is { } stale &&
                (!facts.ControlUseItem || !stale.Matches(member.Connection.Player, member.SelectedItem, selected, item.BaseManaMaximum)))
                item = item with { MagicPayment = stale with { EligibleBirth = false, AwaitingPhase = false } };
            item = item with { Channel = magicUse.Channel, SlowMagicUse = magicUse.SlowMagicUse };
            if (magicUse.RefreshManaDelay) mana = mana with { Delay = 4f };
            use = new(magicUse.State, facts.Life, magicUse.Mana, false, 0, 0)
            { PendingItemReuse = magicUse.PendingItemReuse };
            beganActualUse = magicUse.BeganActualUse;
        }

        else if (VanillaRemoteRangedItemCheck1458.TryGetAmmoFamily(selected.ItemType, out var ammoFamily))
        {
            var rangedFacts = new PlayerRemoteRangedItemFacts1458(selected.ItemType,
                CaptureRemoteRangedAmmo(member, ammoFamily), facts.ControlUseItem, facts.LastUseSuccess,
                facts.Cursed, facts.CrowdControlled, facts.SelectionBuffered, selected.Prefix,
                world.WindowsItemPrefixArithmetic);
            if (!VanillaRemoteRangedItemCheck1458.TryStep(clocks, in rangedFacts, random.Next, out var rangedUse))
                return false;
            use = new(rangedUse.State, facts.Life, facts.Mana, false, 0, 0)
            {
                PendingItemReuse = rangedUse.PendingItemReuse
            };
            beganActualUse = rangedUse.BeganActualUse;
        }
        else if (VanillaRemoteMeleeItemCheck1458.IsSupported(selected.ItemType, selected.Prefix,
            world.WindowsItemPrefixArithmetic))
        {
            var meleeFacts = new PlayerRemoteMeleeItemFacts1458(selected.ItemType, facts.ControlUseItem,
                facts.LastUseSuccess, facts.Cursed, facts.CrowdControlled, facts.SelectionBuffered,
                selected.Prefix, world.WindowsItemPrefixArithmetic);
            if (!VanillaRemoteMeleeItemCheck1458.TryStep(clocks, in meleeFacts, random.Next, out var meleeUse,
                derivedCombat.MeleeAnimationMultiplier))
                return false;
            use = new(meleeUse.State, facts.Life, facts.Mana, false, 0, 0)
            {
                PendingItemReuse = meleeUse.PendingItemReuse,
                ResetItemRotation = meleeUse.ResetItemRotation
            };
            beganActualUse = meleeUse.BeganActualUse;
            mining = meleeUse.IsMiningTool;
        }
        else if (VanillaRemotePassiveItemCheck1458.IsSupported(selected.ItemType, selected.Prefix))
        {
            var passiveFacts = new PlayerRemotePassiveItemFacts1458(selected.ItemType, facts.ControlUseItem,
                facts.LastUseSuccess, facts.Cursed, facts.CrowdControlled, facts.SelectionBuffered, selected.Prefix);
            if (!VanillaRemotePassiveItemCheck1458.TryStep(clocks, in passiveFacts, random.Next, out var passiveUse))
                return false;
            use = new(passiveUse.State, facts.Life, facts.Mana, false, 0, 0)
            {
                PendingItemReuse = passiveUse.PendingItemReuse,
                ResetItemRotation = passiveUse.ResetItemRotation
            };
            beganActualUse = passiveUse.BeganActualUse;
        }
        else if (VanillaSelectedConsumableCatalog1458.TryGet(selected.ItemType, out var selectedPotion) &&
            (selectedPotion.HealLife > 0 || selectedPotion.HealMana > 0))
        {
            if (!TryPlanOwnedSelectedPotion(plan, selected, item, clocks, in facts, random,
                out use, out item)) return false;
            beganActualUse = use.BeganUse;
        }
        else
        {
            if (!VanillaSelectedConsumable1458.TryStep(clocks, in facts, random.Next, random.NextDouble, out use))
                return false;
            beganActualUse = use.BeganUse;
        }
        // ItemCheck clears non-shooting item rotation when attempting use, even if
        // the later success/cursed/potion-delay gate rejects the actual start.
        if (use.ResetItemRotation) plan.Rotation = 0f;
        if (use.PotionSicknessOffer > 0)
            plan.Buffs.TryApplySelectedConsumable(new BuffTypeId(21), use.PotionSicknessOffer, false);
        if (use.ManaSicknessOffer > 0)
            plan.Buffs.TryApplySelectedConsumable(new BuffTypeId(94), use.ManaSicknessOffer, false);
        plan.Health = knownHealth with { Life = use.Life };
        plan.Item = item with
        {
            Mana = mana with { Mana = use.Mana }, Selected = use.State,
            PendingItemReuse = use.PendingItemReuse,
            AttackCD = beganActualUse ? 0 : item.AttackCD,
            ToolTime = beganActualUse && mining ? 1 : item.ToolTime
        };
        plan.Changed = true;
        return true;
    }

    private bool? CaptureRemoteRangedAmmo(RuntimePlayerMember member, VanillaProjectileAmmoFamily family)
    {
        bool unknown = false;
        // Source HasAmmo is an existence query across inventory0..57. A later known match
        // resolves an earlier unknown; cursor58 and the void bag do not participate.
        for (byte slot = 0; slot < 58; slot++)
        {
            if (!inventory.TryGet(member.Connection, slot, out var item) || !item.IsCanonical)
            {
                unknown = true;
                continue;
            }
            if (item.Stack <= 0) continue;
            if (!VanillaItemIds.TryCreate(item.ItemType.Value, out _)) unknown = true;
            else if (VanillaProjectileWeaponCombatCatalog.IsAmmoType(family, item.ItemType)) return true;
        }
        return unknown ? null : false;
    }

    private static ServerPlayerHorizontalIntent ResolveRemoteHorizontalIntent(bool left, bool right, float velocityX)
    {
        // Player.HorizontalMovement (1.4.5.8) tests left below its run-speed cap before right.
        // Both remote controls remain set: at or beyond the negative cap the right branch wins.
        // This phase admits neutral equipment only, so both source caps are the baseline cap.
        var profile = VanillaServerPlayerHorizontalProfile1458.Baseline;
        if (left && (!right || velocityX > -profile.MaximumRunSpeed))
            return ServerPlayerHorizontalIntent.Left;
        return right ? ServerPlayerHorizontalIntent.Right : ServerPlayerHorizontalIntent.Stop;
    }

    private sealed class RemoteItemPhasePlan1458(RuntimePlayerMember member, PlayerStateSnapshot before,
        RuntimePlayerItemPhase1458? item, RuntimePlayerPhysicsPhase1458? physics,
        PlayerAppearanceCommitRequest? appearance, PlayerEquipmentCommitRequest[] equipment,
        BuffTypeId[] buffTypes, PlayerBuffState buffs, bool godMode, bool ownsManaMaximum)
    {
        internal RuntimeTownShopSession1458? BeforeShopSession { get; set; }
        internal RuntimePlayerInventoryItem[] InventoryBefore { get; } = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        internal List<RemotePhaseInventoryMutation> InventoryMutations { get; } = [];
        internal bool PublishMana { get; set; }
        internal bool PublishHealth { get; set; }
        internal bool PublishBuffs { get; set; }
        internal TerraRuntime.Contracts.Gameplay.BuffTypeId[]? PublishedBuffTypes { get; set; }
        internal RuntimePlayerMember Member { get; } = member;
        internal PlayerStateSnapshot Before { get; } = before;
        internal RuntimePlayerItemPhase1458? BeforeItem { get; } = item;
        internal RuntimePlayerPhysicsPhase1458? BeforePhysics { get; } = physics;
        internal PlayerAppearanceCommitRequest? Appearance { get; } = appearance;
        internal PlayerEquipmentCommitRequest[] Equipment { get; } = equipment;
        internal BuffTypeId[] BuffTypes { get; } = buffTypes;
        internal PlayerBuffState BeforeBuffs { get; } = buffs;
        internal bool GodMode { get; } = godMode;
        internal bool BeforeOwnsManaMaximum { get; } = ownsManaMaximum;
        internal PlayerBuffState Buffs { get; } = buffs.Clone();
        internal RuntimePlayerItemPhase1458? Item { get; set; } = item;
        internal RuntimePlayerPhysicsPhase1458? Physics { get; set; } = physics;
        internal PlayerNpcHealthState1458? Health { get; set; } = before.NpcHealth;
        internal PlayerNpcHealthWorld1458? HealthWorld { get; set; }
        internal bool? Grappling { get; set; }
        internal int? Maximum { get; set; } = before.DerivedLifeMax;
        internal PlayerDebuffSnapshot1458? Debuffs { get; set; } = before.Debuffs;
        internal float X { get; set; } = before.PositionX;
        internal float Y { get; set; } = before.PositionY;
        internal float Vx { get; set; } = before.VelocityX;
        internal float Vy { get; set; } = before.VelocityY;
        internal float Rotation { get; set; } = member.ItemRotation;
        internal bool Changed { get; set; }
    }
}
