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
        if (serverPlayers?.CopySnapshots(bots) > 0)
            return false;
        // An empty update does not read world/profile facts or consume the player stream.
        // Keep the normal no-player tick allocation-free without reviving retired custody.
        if (membership.Count == 0)
            return true;
        // Main.UpdateWorld_Players never updates its dedicated local slot255.
        var members = membership.Members.Where(static member => member.Slot.Value < byte.MaxValue).OrderBy(static member => member.Slot.Value).ToArray();
        if (members.Length == 0)
            return playerUpdateRandomOwned && playerUpdateRandom is not null;
        if (!playerUpdateRandomOwned ||
            playerUpdateRandom is not { } random ||
            playerUpdateWorld is not { } world ||
            worldTiles is not { } tiles ||
            !world.IsValid ||
            membership.Serial == ulong.MaxValue ||
            inventory.Serial == ulong.MaxValue ||
            tiles.Dimensions.SectionCount > 4_096)
            return false;
        // A retired observation is not an unrepresented Player.Update. Controls,
        // selection and other ingress may cancel pending input without poisoning
        // the known shared frame stream or replaying a raw world mutation.
        foreach (var member in members) RetireStaleToolInput(member);
        ulong memberSerial = membership.Serial;
        ulong inventorySerial = inventory.Serial;
        var favorPrelude = torchFavorPrelude;
        ulong favorRevision = favorPrelude?.Revision ?? 0;
        var beforeRandom = random.Clone();
        var plannedRandom = random.Clone();
        var versions = new long[tiles.Dimensions.SectionCount];
        for (int i = 0; i < versions.Length; i++)
        {
            versions[i] = tiles.GetSectionVersion(TerrariaSectionGeometry.FromLinearIndex(tiles.Dimensions, i));
            if (versions[i] == long.MaxValue || (versions[i] & 1) != 0)
                return false;
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
            if (!transferProfiles.TryCapture(member.Connection,
                out var appearance,
                out var equipment,
                out var buffs) ||
                buffs is null ||
                transferProfiles.CaptureBuffState(member.Connection)is not { } buffState)
                return false;
            plans[i] = new(member,
                member.CaptureSnapshot(),
                member.ItemPhase,
                member.PhysicsPhase,
                appearance,
                equipment,
                buffs,
                buffState,
                member.GodMode,
                member.OwnsManaMaximum,
                member.OwnsLifeMaximum);
            membership.TryGetTownShopSession(member.Connection.Player, out var shopSession);
            plans[i].BeforeShopSession = shopSession;
            plans[i].BeforePermanentUpgradeMask = member.OwnedPermanentUpgradeMask;
            plans[i].BeforeOwnsTorchFavor = member.OwnsTorchFavor;
            plans[i].BeforePaidTorchFavor = member.PaidTorchFavor;
            if (!inventory.TryCopyInventory(member.Connection, plans[i].InventoryBefore))
                return false;
            var oldAmmoHistory=member.ItemPhase?.ToolUse?.WeaponShootHistory;
            if(oldAmmoHistory is not null || member.ItemUseCaller==RuntimeItemUseCaller1458.LocalOwnerProjection &&
                inventory.TryGet(member.Connection,member.SelectedItem,out var localWeapon) && IsOrderedAmmoWeapon(localWeapon))
            {
                if(!inventory.TryCaptureShopJournal(member.Connection,out var ammoBefore) || ammoBefore is null)return false;
                plans[i].AmmoInventoryBefore=ammoBefore;
                if(oldAmmoHistory?.InventoryWitness is {IsCurrent:false})
                    plans[i].RefreshedAmmoHistory=RefreshUnpaidAmmoInputHistory(oldAmmoHistory,ammoBefore);
            }
            // Capture source UpdateEquips' bag gate before any external provider.
            // Cursor 58 and stored bags cannot open Void storage.
            for (int slot = 0; slot < 58; slot++)
                if (VanillaVoidStorageCatalog1458.OpensVoidStorage(plans[i].InventoryBefore[slot].ItemType))
                    plans[i].VoidStorageOpen = true;
        }

        foreach (var plan in plans)
        {
            bool outside = VanillaPlayerHealthContext1458.IsRemoteOutOfRange(plan.X, plan.Y, 20, 42, world.MaxTilesX, world.MaxTilesY);
            bool ghost = (plan.Before.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0;
            if (outside || ghost || plan.Before.IsDead)
                continue;
            if (ownedHealthWorld is not { } healthWorld || ownedProjectiles is null)
                return false;
            plan.HealthWorld = healthWorld;
            plan.Grappling = CaptureRemotePlayerGrappling(plan.Before.Player);
            // The ordinary physics owner has no retained hook attachment/movement phase.
            if (plan.Grappling != false ||
                (healthWorldSource is not null &&
                healthWorldSource() != healthWorld) ||
                (grapplingSource is not null &&
                grapplingSource(plan.Before.Player) != plan.Grappling))
                return false;
        }

        var physics = new VanillaServerPlayerDryPhysicsStepper(tiles);
        foreach (var plan in plans)
        {
            if (!TryPlanRemoteItemPhase(plan, in world, physics, plannedRandom))
                return false;
            if (plan.AmmoCyclingAfterShoot is { } selectedAmmoState && plan.Item is { } acceptedItem)
                plan.Item = acceptedItem with { AmmoCycling = selectedAmmoState };
        }

        // Finish every external read before the final owned-store comparisons. In particular a
        // later player's provider can mutate an earlier player's indexed buff durations.
        foreach (var plan in plans)
            if (plan.HealthWorld is not null && grapplingSource is not null && grapplingSource(plan.Before.Player) != plan.Grappling)
                return false;
        foreach (var plan in plans)
            if (plan.HealthWorld is { } healthWorld && healthWorldSource is not null && healthWorldSource() != healthWorld)
                return false;
        foreach (var plan in plans)
            if (plan.HealthWorld is not null && CaptureRemotePlayerGrappling(plan.Before.Player) != plan.Grappling)
                return false;
        // No external readers occur after this tail. Adoption cannot expose a partial census.
        if (membership.Serial != memberSerial ||
            inventory.Serial != inventorySerial ||
            !playerUpdateRandomOwned ||
            !ReferenceEquals(playerUpdateRandom,
            random) ||
            !ReferenceEquals(npcHealthWorldFacts,
            healthWorldSource) ||
            !ReferenceEquals(npcHealthGrapplingFacts,
            grapplingSource) ||
            remotePlayerHealthWorld != ownedHealthWorld ||
            !ReferenceEquals(remotePlayerProjectiles,
            ownedProjectiles) ||
            !random.HasSameState(beforeRandom) ||
            playerUpdateWorld != world ||
            combatEquipmentExpertMode != equipmentExpert ||
            combatEquipmentMasterMode != equipmentMaster ||
            combatEquipmentLocalVanityArmor != localVanity ||
            serverPlayers?.CopySnapshots(bots) > 0)
            return false;
        for (int i = 0; i < versions.Length; i++)
            if (tiles.GetSectionVersion(TerrariaSectionGeometry.FromLinearIndex(tiles.Dimensions, i)) != versions[i])
                return false;
        foreach (var plan in plans)
        {
            var member = plan.Member;
            membership.TryGetTownShopSession(member.Connection.Player, out var shopSession);
            if (!ReferenceEquals(shopSession,
                plan.BeforeShopSession) ||
                !membership.TryGet(member.Connection,
                out var current) ||
                !ReferenceEquals(current,
                member) ||
                member.CaptureSnapshot() != plan.Before ||
                member.ItemPhase != plan.BeforeItem ||
                member.PhysicsPhase != plan.BeforePhysics ||
                member.GodMode != plan.GodMode ||
                member.OwnedPermanentUpgradeMask != plan.BeforePermanentUpgradeMask ||
                member.OwnsTorchFavor != plan.BeforeOwnsTorchFavor ||
                member.PaidTorchFavor != plan.BeforePaidTorchFavor ||
                member.OwnsManaMaximum != plan.BeforeOwnsManaMaximum ||
                member.OwnsLifeMaximum != plan.BeforeOwnsLifeMaximum ||
                member.Revision == ulong.MaxValue ||
                member.ItemUseCaller!=plan.BeforeItemUseCaller ||
                plan.AmmoInventoryBefore is {IsCurrent:false} ||
                plan.RefreshedAmmoHistory is null && plan.BeforeItem?.ToolUse?.WeaponShootHistory?.InventoryWitness is { IsCurrent: false } ||
                plan.FishingBefore is { } fishingBefore && toolProjectiles?.IsFishingCurrent(fishingBefore) != true ||
                plan.ToolSourceQuery is { IsCurrentOwned: false } ||
                plan.Item?.ToolUse?.BuildingPermission is { IsCurrent: false } ||
                plan.Item?.ToolUse?.PlacementAlternative is { Reads.IsCurrent: false } ||
                plan.PlacementChosenAlternative is { } chosenPlacement &&
                    (!IsPlacementAlternativeContext(plan, chosenPlacement, chosenPlacement.World) ||
                     playerUpdateRandom is not { } placementRandom || !chosenPlacement.MatchesCanonical(placementRandom)) ||
                plan.BeforeToolInput is { } input &&
                    !IsCurrentToolInput(input, plan.BeforeToolInputRevision) ||
                !transferProfiles.TryCapture(member.Connection,
                out var appearance,
                out var equipment,
                out var buffs) ||
                appearance != plan.Appearance ||
                !equipment.AsSpan().SequenceEqual(plan.Equipment) ||
                buffs is null ||
                !buffs.AsSpan().SequenceEqual(plan.BuffTypes) ||
                !transferProfiles.IsCurrentBuffState(member.Connection,
                plan.BeforeBuffs))
                return false;
        }

        RuntimeNpcDeathPrelude1458? favorPreview = null;
        int favorCount = plans.Count(static plan => plan.CreditTorchFavor);
        if (favorCount != 0)
        {
            if (!ReferenceEquals(torchFavorPrelude, favorPrelude) || favorPrelude is null || favorPrelude.Revision != favorRevision)
                return false;
            var victims = new NpcSnapshot[favorCount];
            Array.Fill(victims, new(default, default, 664, 664, 0f, 0f, 0f, 0f, 0, default, default));
            if (!favorPrelude.TryPrepareScriptedKills(victims, out favorPreview))
                return false;
        }

        foreach (var plan in plans)
            if (plan.PublishBuffs)
                plan.PublishedBuffTypes = plan.Buffs.CaptureTypes();
        var phaseMutations = plans.SelectMany(static plan => plan.InventoryMutations).ToArray();
        if (!inventory.TryPrepareRemotePhase(phaseMutations, inventorySerial, out var inventoryPlan) || !inventoryPlan!.TryAdoptUnpublished())
            return false;
        RuntimeNpcDeathPrelude1458.PreludePublication? favorPublication = null;
        if (favorPreview is not null && !favorPrelude!.TryAdoptUnpublished(favorPreview, favorRevision, out favorPublication))
            throw new InvalidOperationException("Validated Torch favor prelude lost currentness during callback-free adoption.");
        foreach (var plan in plans)
        {
            if (!plan.Changed)
                continue;
            var member = plan.Member;
            // Every condition was checked above; these owned assignments invoke no callbacks.
            transferProfiles.TryAdoptBuffState(member.Connection, plan.BeforeBuffs, plan.Buffs);
            if (plan.AcceptedPermanentAppearance is { } acceptedAppearance)
                transferProfiles.TrySetAppearance(member.Connection, in acceptedAppearance);
            if (plan.CreditTorchFavor)
                member.PaidTorchFavor = true;
            member.TryAdvanceRemotePhaseRevision();
            member.ItemPhase = plan.Item;
            if(plan.BeforeItem?.ToolUse?.WeaponShoot?.PendingManaReport is not null &&
                plan.Item?.ToolUse?.WeaponShoot?.PendingManaReport is null)
                RejectedManaUpdates++; // Accepted whole-phase retirement, once before observers.
            if (plan.InventoryMutations.Any(static mutation => mutation.CursorSourceDebit is not null) &&
                member.CursorMove?.Inventory is { } consumed &&
                plan.InventoryMutations.Any(mutation => ReferenceEquals(mutation.CursorSourceDebit,
                consumed)))
                member.CursorMove = null;
            ObserveShopPricingPhase(member);
            if (plan.Item?.MagicPayment is not null)
                member.OwnsManaMaximum = true;
            member.PhysicsPhase = plan.Physics;
            member.ItemAnimation = plan.Item!.Value.Selected.Animation;
            member.ItemRotation = plan.Rotation;
            member.NpcHealth = plan.Health;
            member.NpcLifeCurrent = plan.Health is not null;
            member.DerivedLifeMax = plan.Maximum;
            if (plan.AcceptedBaseLifeMaximum is { } acceptedBase)
            {
                member.BaseLifeMax = acceptedBase;
                member.MaxLife = checked((short)acceptedBase);
                member.OwnsLifeMaximum = true;
            }

            member.Debuffs = plan.Debuffs;
            member.Mana = checked((short)plan.Item.Value.Mana.Mana);
            if (plan.PublishMana)
                member.MaxMana = checked((short)plan.Item.Value.BaseManaMaximum);
            member.PositionX = plan.X;
            member.PositionY = plan.Y;
            member.VelocityX = plan.Vx;
            member.VelocityY = plan.Vy;
            member.MarkRemotePhaseSnapshot();
        }

        random.CopyStateFrom(plannedRandom);
        foreach(var acceptedPlan in plans)
        {
            RebindAcceptedAmmoHistory(acceptedPlan.Member);
            // Inventory-witness rebinding changes only owned metadata. Keep
            // later publication guards on this callback-free adopted image.
            acceptedPlan.Item=acceptedPlan.Member.ItemPhase;
        }
        foreach (var plan in plans)
        {
            // Rebind only an exact receipt whose complete before-image and
            // observation revision passed the final preflight above. The saved
            // ItemCheck-stage pose stays in ToolUse; currentness follows the
            // accepted whole-phase actor image, not an arbitrary client rewrite.
            if (plan.BeforeToolInput is not { } input ||
                plan.BeforeToolInputActor is not { } inputActor ||
                plan.Member.ItemPhase is not { ToolUse: { } tools }) continue;
            if (!ReferenceEquals(tools.Input, input))
            {
                continue;
            }
            if (tools.SourceStagePose is null || tools.LastFrameUpdate != currentCombatTick ||
                !TryCaptureToolActor(plan.Member.Connection, out var accepted) || accepted is null ||
                !input.TryRebaseAfterOwnedPhase(inputActor, plan.BeforeToolInputRevision, accepted))
            {
                RetireToolInput(plan.Member, input);
            }
        }
        Exception? first = null;
        try { DrainFishingActionsAfterOwnedPhase(); }
        catch (Exception error) { first = error; }
        try { toolWorld?.DrainOrderedLocalMagicBirths(); }
        catch(Exception error) {first??=error;}
        using var toolPublications = toolWorld?.PrepareToolBirthsAfterOwnedPhase();
        if (toolPublications is { } toolBatch)
        {
            try { CollectToolBaselineHealthPublications(toolBatch.HealthPublications); }
            catch (Exception error) { first ??= error; }
            try { toolWorld!.DrainToolActionsAfterOwnedPhase(toolBatch); }
            catch (Exception error) { first ??= error; }
            try { CollectToolBaselineHealthPublications(toolBatch.HealthPublications); }
            catch (Exception error) { first ??= error; }
            try { toolBatch.TryPublishAll(); }
            catch (Exception error) { first ??= error; }
        }
        try
        {
            favorPublication?.TryPublish();
        }
        catch (Exception error)
        {
            first ??= error;
        }

        int mutationIndex = 0;
        for (int index = 0; index < plans.Length; index++)
        {
            var plan = plans[index];
            foreach (var mutation in plan.InventoryMutations)
            {
                int journalIndex = mutationIndex++;
                if (membership.TryGet(mutation.Connection, out var current) && ReferenceEquals(current, plan.Member) && inventoryPlan.IsAcceptedItemCurrent(journalIndex))
                {
                    try
                    {
                        var report = mutation.After.ToCommitRequest(mutation.Connection.Player.Slot, mutation.Slot);
                        events?.PlayerEquipmentUpdated(mutation.Connection, in report);
                    }
                    catch (Exception error)
                    {
                        first ??= error;
                    }
                }
            }

            if (plan.AcceptedPermanentAppearance is { } publishedAppearance &&
                membership.TryGet(plan.Member.Connection,
                out var appearanceMember) &&
                ReferenceEquals(appearanceMember,
                plan.Member) &&
                transferProfiles.TryCapture(plan.Member.Connection,
                out var currentAppearance,
                out _,
                out _) &&
                currentAppearance == publishedAppearance)
            {
                try
                {
                    events?.PlayerAppearanceUpdated(plan.Member.Connection, in publishedAppearance);
                }
                catch (Exception error)
                {
                    first ??= error;
                }
            }

            if (plan.PublishHealth &&
                membership.TryGet(plan.Member.Connection,
                out var healthMember) &&
                ReferenceEquals(healthMember,
                plan.Member) &&
                healthMember.NpcHealth == plan.Health &&
                IsAmmoDeliveryPublicationAfterimage(healthMember.ItemPhase,plan.Item))
            {
                try
                {
                    var report = new PlayerHealthCommitRequest(plan.Member.Slot, checked((short)plan.Health!.Value.Life), healthMember.MaxLife);
                    events?.PlayerHealthUpdated(plan.Member.Connection, in report);
                }
                catch (Exception error)
                {
                    first ??= error;
                }
            }

            if (plan.PublishMana &&
                membership.TryGet(plan.Member.Connection,
                out var manaMember) &&
                ReferenceEquals(manaMember,
                plan.Member) &&
                manaMember.Mana == plan.Item!.Value.Mana.Mana &&
                manaMember.MaxMana == plan.Item.Value.BaseManaMaximum &&
                manaMember.ItemPhase?.BaseManaMaximum == plan.Item.Value.BaseManaMaximum)
            {
                try
                {
                    var report = new PlayerManaCommitRequest(plan.Member.Slot, manaMember.Mana, manaMember.MaxMana);
                    events?.PlayerManaUpdated(plan.Member.Connection, in report);
                }
                catch (Exception error)
                {
                    first ??= error;
                }
            }

            if (plan.PublishedBuffTypes is { } publishedBuffs &&
                membership.TryGet(plan.Member.Connection,
                out var buffMember) &&
                ReferenceEquals(buffMember,
                plan.Member) &&
                transferProfiles.IsCurrentBuffState(plan.Member.Connection,
                plan.Buffs))
            {
                try
                {
                    var report = new PlayerBuffTypesCommitRequest(plan.Member.Slot, publishedBuffs);
                    events?.PlayerBuffTypesUpdated(plan.Member.Connection, in report);
                }
                catch (Exception error)
                {
                    first ??= error;
                }
            }
        }

        if (first is not null)
            ExceptionDispatchInfo.Capture(first).Throw();
        return true;
    }

    private bool TryPlanRemoteItemPhase(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerUpdateWorld1458 world,
        VanillaServerPlayerDryPhysicsStepper physics,
        VanillaUnifiedRandom1458 random)
    {
        plan.SourceRandom = random;
        var member = plan.Member;
        var effectTypes = plan.BeforeBuffs.CaptureEffectTypes();
        if (plan.Item is not { } item ||
            (item.ShopDiscountStates & ~15) != 0 ||
            item.ClientUiPriceArithmeticMask > 3 ||
            plan.Physics is not { } physical ||
            item.DeadTime is < 0 or >= int.MaxValue ||
            item.RespawnTimer < 0 ||
            item.ManaPotionDelay < 0 ||
            member.NpcHealth is not { SourceProfileKnown: true } ||
            member.HasMount ||
            plan.GodMode ||
            ((plan.Appearance?.ConsumableUnlockFlags ?? 0) & ~127) != 0 ||
            !TryCaptureProvenRemoteEquipment(plan.Equipment,
            plan.Appearance,
            out var equipmentCombat,
            out int manaMaximumBonus,
            plan.VoidStorageOpen, item.AmmoCycling is not null) ||
            plan.Buffs.IsItemUseCrowdControlled ||
            plan.BuffTypes.Any(static type => type.Value is not (5 or 16 or 20 or 21 or 23 or 24 or 39 or 93 or 94 or 112 or 114 or 115 or 117 or 321 or 26 or 206 or 207 or 121 or 122 or 123)) ||
            member.MiscFlags1 != 0 ||
            (member.MiscFlags2 & ~((1 << 6) | (1 << 7))) != 0 ||
            (member.ControlFlags & ~0x7c) != 0 ||
            (member.MovementFlags & ~0x54) != 0 ||
            physical.GravityDirection is not (1f or -1f) ||
            item.Selected.Animation != member.ItemAnimation)
            return false;
        // Source-local Update_AmmoCycler precedes ghost/dead exits and does
        // not depend on which item is selected or whether equipment was removed.
        if (plan.BeforeItemUseCaller == RuntimeItemUseCaller1458.LocalOwnerProjection && item.AmmoCycling is { } cycling)
            item = item with { AmmoCycling = VanillaAmmoCycling1458.AdvanceLocalUpdate(in cycling) };
        if(plan.RefreshedAmmoHistory is { } retiredAmmo && item.ToolUse is { } retainedTools)
            item=item with {ToolUse=retainedTools with {WeaponShootHistory=retiredAmmo}};
        // Packet42 and accepted projectile mana debits write the owned current value. Neither
        // update resets the retained source regeneration count or delay.
        item = item with
        {
            Mana = item.Mana with
            {
                Mana = member.Mana
            }
        };
        bool outside = VanillaPlayerHealthContext1458.IsRemoteOutOfRange(plan.X, plan.Y, 20, 42, world.MaxTilesX, world.MaxTilesY);
        bool ghost = (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0;
        if (member.IsDead && plan.Buffs.HasOwnedFoodSlots)
        {
            plan.Buffs.ClearOwnedFood();
            plan.PublishBuffs = true;
        }

        if (!member.IsDead)
            plan.PublishBuffs |= plan.Buffs.AdvanceOwnedFoodClock(currentCombatTick, !outside);
        plan.Maximum = VanillaPlayerHealthContext1458.Resolve(member.DerivedLifeMax, member.BaseLifeMax, 0, outside, ghost, member.IsDead);
        plan.Debuffs = VanillaPlayerHealthContext1458.ResolveDebuffs(member.Debuffs, plan.Buffs.CaptureDebuffFlags(), outside, ghost, member.IsDead);
        int delay = item.Selected.PotionDelay;
        if (outside)
        {
            if (item.MagicPayment is { } payment)
                item = item with
                {
                    MagicPayment = payment with
                    {
                        EligibleBirth = false,
                        AwaitingPhase = false,
                        MidpointEligible = false
                    }
                };
            bool combatBuff = effectTypes.Any(static type => type.Value is 5 or 16 or 114 or 115 or 117 or 321 or 26 or 206 or 207);
            if (combatBuff)
            {
                if (item.DerivedCombat is not { } retained ||
                    !IsValidDerivedCombat(in retained) ||
                    !VanillaPlayerCombatBuffs1458.TryApply(in retained,
                    effectTypes,
                    out var compounded) ||
                    !IsValidDerivedCombat(in compounded))
                    return false;
                item = item with
                {
                    DerivedCombat = compounded
                };
            }

            int duration = plan.Buffs.GetLastActiveDuration(new BuffTypeId(21));
            if (duration > 0)
                delay = duration;
            plan.Item = item with
            {
                Selected = item.Selected with
                {
                    Animation = 0,
                    PotionDelay = delay
                },
                ToolUse = item.ToolUse is { } outsideTool ? outsideTool with
                {
                    Clocks = outsideTool.Clocks with
                    {
                        Selected = outsideTool.Clocks.Selected with { Animation = 0 }
                    },
                    Input = outsideTool.Input,
                    ShootEligible = false,
                    ToolStagePending = false
                } : null
            };
            plan.Changed = true;
            return true;
        }

        if (!VanillaSelectedConsumable1458.TryStepHeat(item.ManaHeat, random.NextDouble, random.Next, out float heat))
            return false;
        if (item.ToolTime is not >= 0 || !VanillaRemoteMeleeItemCheck1458.TryStepAttackCooldown(item.AttackCD, item.Selected.Animation, out int attackCD))
            return false;
        delay = Math.Max(0, delay - 1);
        item = item with
        {
            ManaHeat = heat,
            AttackCD = attackCD,
            ManaPotionDelay = Math.Max(0, item.ManaPotionDelay - 1),
            Selected = item.Selected with
            {
                PotionDelay = delay
            }
        };
        if (ghost || member.IsDead)
        {
            if (item.MagicPayment is { } payment)
                item = item with
                {
                    MagicPayment = payment with
                    {
                        EligibleBirth = false,
                        AwaitingPhase = false,
                        MidpointEligible = false
                    }
                };
            // Stationary ghost/dead profiles with retained nonconstructor clocks are independently captured.
            // Moving ghosts and respawn side effects require their own retained state.
            if (ghost && (plan.Vx != 0f || plan.Vy != 0f || (member.ControlFlags & 0x1c) != 0))
                return false;
            // Dedicated UpdateDead can turn a hardcore player into a ghost when the respawn
            // clock expires. This slice does not own surviveHardcoreDeath or the ghost transition.
            if (!ghost && plan.Appearance is { DifficultyFlags: var difficulty } && (difficulty & 2) != 0 && (difficulty & 8) == 0)
                return false;
            if (member.IsDead && !ghost)
            {
                plan.Buffs.ClearNonPersistentOnDeath();
                plan.Health = member.NpcHealth!.Value with
                {
                    Life = 0
                };
                item = item with
                {
                    ManaHeat = 1f,
                    Channel = false,
                    ToolUse = item.ToolUse is { } deadTool ? deadTool with
                    {
                        Clocks = deadTool.Clocks with
                        {
                            Selected = deadTool.Clocks.Selected with { Animation = 0 },
                            Channel = false
                        },
                        Input = deadTool.Input,
                        ShootEligible = false,
                        ToolStagePending = false
                    } : null,
                    ManaPotionDelay = 0,
                    DeadTime = item.DeadTime + 1,
                    RespawnTimer = Math.Clamp(item.RespawnTimer - 1, 0, 3_600),
                    Selected = item.Selected with
                    {
                        Animation = 0,
                        PotionDelay = 0
                    }
                };
                plan.Physics = physical with
                {
                    GravityDirection = 1f
                };
            }

            if (ghost)
                // Source Ghost() has its own crit reset; dead/outside retain their prior fields.
                item = item with
                {
                    Mana = item.Mana with
                    {
                        Maximum = item.BaseManaMaximum
                    },
                    DerivedCombat = VanillaPlayerCombatSnapshot.Baseline
                };
            plan.Item = item;
            plan.Changed = true;
            return true;
        }

        if (!inventory.TryGet(member.Connection, member.SelectedItem, out var selected))
            return false;
        if (item.SelectedPotionUse is { Completed: false } provisional &&
            provisional.Slot == member.SelectedItem &&
            provisional.Player == member.Connection.Player &&
            member.CursorMove?.Inventory is { } potionDebit &&
            potionDebit.TryCaptureSelectedPotionDebit(inventory,
            member.Connection,
            out var originalPotion,
            out _) &&
            originalPotion == provisional.Before)
            selected = originalPotion;
        if (!TryChoosePlacementAlternative(plan, world, random, ref item)) return false;
        if (!selected.IsCanonical ||
            (!(selected.Prefix == VanillaPrefixIds.None &&
            VanillaSelectedConsumableCatalog1458.TryGet(selected.ItemType,
            out _)) &&
            !VanillaRemoteRangedItemCheck1458.IsSupported(selected.ItemType,
            selected.Prefix,
            world.WindowsItemPrefixArithmetic) &&
            !VanillaRemoteMeleeItemCheck1458.IsSupported(selected.ItemType,
            selected.Prefix,
            world.WindowsItemPrefixArithmetic) &&
            !VanillaRemotePassiveItemCheck1458.IsSupported(selected.ItemType,
            selected.Prefix) &&
            !VanillaRemoteChannelledMagic1458.IsSupported(selected.ItemType,
            selected.Prefix,
            world.WindowsItemPrefixArithmetic) &&
            !VanillaGemStaff1458.TryGetWeapon(selected.ItemType,
            out _) && !VanillaToolCatalog1458.TryGet(selected.ItemType, out _) &&
            !(selected.Prefix.Value == 0 && (VanillaWiringToolCatalog1458.TryGet(selected.ItemType, out _) ||
                FishingRules1458.TryGetPole(selected.ItemType.Value, out _)))))
            return false;
        if (VanillaRemoteChannelledMagic1458.IsSupported(selected.ItemType,
            selected.Prefix,
            world.WindowsItemPrefixArithmetic) &&
            (item.BaseManaMaximum <= 0 ||
            item.MagicPayment is { } malformedPayment &&
            !malformedPayment.IsValid))
            return false;
        if (!VanillaToolCatalog1458.TryGet(selected.ItemType, out _) &&
            !VanillaWiringToolCatalog1458.TryGet(selected.ItemType, out _) &&
            !VanillaLocalPlacementCatalog1458.TryGet(selected.ItemType, out _) && item.ToolUse is { } unusedTools &&
            (!unusedTools.SourceItem.IsNone || unusedTools.BuildingPermission is not null ||
                unusedTools.SourceStagePose is not null || unusedTools.ShootEligible || unusedTools.ToolStagePending))
            item = item with
            {
                ToolUse = unusedTools with
                {
                    BuildingPermission = null,
                    SourceStagePose = null,
                    RequiresFreshUse = true,
                    ShootEligible = false,
                    ToolStagePending = false
                }
            };
        var critArithmetic = world.WindowsItemPrefixArithmetic ? VanillaBulletSourceArithmetic1458.WindowsClr4X86 : VanillaBulletSourceArithmetic1458.CoreClrSingle;
        if (!VanillaSelectedItemCrit1458.TryResolve(selected.ItemType, selected.Prefix, critArithmetic, world.RemixWorld, out int itemCrit))
            return false;
        // ResetEffects derives all three fields from this phase's selected Item.crit.
        // Later selection/equipment reports must not reconstruct these retained values.
        // UpdateBuffs precedes the final magic-damage heat/sickness writer; ItemCheck's
        // new mana-potion buff is offered afterward and first affects the following phase.
        int sicknessDuration = plan.Buffs.GetLastActiveDuration(new BuffTypeId(94));
        VanillaPlayerCombatSnapshot buffCombat;
        bool foodEffects = effectTypes.Any(static type => type.Value is 26 or 206 or 207);
        if (foodEffects || equipmentCombat.AmmoCyclerDamage)
        {
            if (!TryBuildSourceOrderedFoodCombat(plan.Equipment, plan.Appearance, effectTypes, out buffCombat, item.AmmoCycling is not null))
                return false;
        }
        else if (!VanillaPlayerCombatBuffs1458.TryApply(in equipmentCombat, effectTypes, out buffCombat))
            return false;
        if (!VanillaPlayerCombatBuffs1458.TryResolveMagicDamage(buffCombat.MagicDamage, sicknessDuration, heat, world.WindowsItemPrefixArithmetic, out float magicDamage))
            return false;
        var derivedCombat = buffCombat with
        {
            Defense = buffCombat.Defense,
            MeleeCrit = buffCombat.MeleeCrit + itemCrit,
            RangedCrit = buffCombat.RangedCrit + itemCrit,
            MagicCrit = buffCombat.MagicCrit + itemCrit,
            MagicDamage = magicDamage
        };
        var capturedUnlocks = new PlayerPermanentUnlockState1458(plan.Appearance is { } phaseAppearance ? PermanentUpgradeFlags(in phaseAppearance) : (byte)0);
        if (member.OwnedPermanentUpgradeMask != 0)
            item = item with
            {
                PermanentPhaseFlags = capturedUnlocks.Flags
            };
        if (capturedUnlocks.Flags != 0 || item.PermanentBoosters is not null || member.OwnedPermanentUpgradeMask != 0 || buffCombat.WellFed)
        {
            var resetBoosters = new PlayerPermanentBoosterState1458(derivedCombat.Defense, 0, buffCombat.PickSpeed, 1f, 1f);
            if (!VanillaSelectedConsumable1458.TryApplyPermanentBoosters(in resetBoosters,
                in capturedUnlocks,
                out var boosters) ||
                !VanillaSelectedConsumable1458.TryFinalizePermanentSpeeds(in boosters,
                out var speeds) ||
                !VanillaSelectedConsumable1458.TryGetPermanentCraftingRegion(plan.X,
                plan.Y,
                20,
                42,
                5,
                3,
                in capturedUnlocks,
                out var craftingRegion))
                return false;
            derivedCombat = derivedCombat with
            {
                Defense = boosters.Defense
            };
            item = item with
            {
                PermanentBoosters = boosters,
                PermanentSpeeds = speeds,
                PermanentCraftingRegion = craftingRegion
            };
        }

        if (!IsValidDerivedCombat(in derivedCombat))
            return false;
        // UpdateEquips precedes this writer in original Player.Update. An open
        // shop retains the prior availability even when functional gear changes.
        item = (item with
        {
            DerivedCombat = derivedCombat
        }

        ).AdvanceShopDiscount(derivedCombat.DiscountEquipped);
        // DD2's source NoBuilding derivation precedes movement and ItemCheck.
        // Retain that independent owned context, not a post-movement arena query
        // or raw buff199. The later tool stage retains its own moved pose.
        bool localPlacementContext = false;
        bool localWiringContext = false;
        if (toolWorld is not null && VanillaToolCatalog1458.TryGet(selected.ItemType, out _))
        {
            if (item.ToolUse is not { } toolBefore ||
                !TryCaptureToolBuildingBeforeMovement(plan.X + VanillaBasePlayerWidth * 0.5f,
                    plan.Y + VanillaBasePlayerHeight * 0.5f, out var permission)) return false;
            item = item with { ToolUse = toolBefore with { BuildingPermission = permission } };
        }
        else if (toolWorld is not null && selected.Prefix.Value == 0 && world.JourneyMode is false &&
            !capturedUnlocks.ArtisanLoaf && item.ToolUse is { } wiringShadow &&
            VanillaWiringToolCatalog1458.TryGet(selected.ItemType, out _) &&
            TryCaptureToolBuildingBeforeMovement(plan.X + VanillaBasePlayerWidth * 0.5f,
                plan.Y + VanillaBasePlayerHeight * 0.5f, out var wiringPermission))
        {
            // Wiring does not consult noBuilding. This capture supplies world/phase currentness only.
            item = item with { ToolUse = wiringShadow with { BuildingPermission = wiringPermission } };
            localWiringContext = true;
        }
        else if (toolWorld is not null && selected.Prefix.Value == 0 &&
            world.JourneyMode is false && item.ToolUse is { } placementShadow &&
            VanillaLocalPlacementCatalog1458.TryGet(selected.ItemType, out var localPlacement) &&
            WorldTileAuthority.SupportsLocalPlacementDefinition(localPlacement) &&
            TryCaptureToolBuildingBeforeMovement(plan.X + VanillaBasePlayerWidth * 0.5f,
                plan.Y + VanillaBasePlayerHeight * 0.5f, out var placementPermission))
        {
            // Missing local context closes world actions, not the independent
            // already-supported dedicated remote census for metadata-only use.
            item = item with { ToolUse = placementShadow with { BuildingPermission = placementPermission } };
            localPlacementContext = true;
        }
        if (plan.Maximum is not { } maximum ||
            plan.HealthWorld is not { } capturedHealthWorld ||
            !TryPlanNpcHealth(member,
            maximum,
            false,
            false,
            out var health,
            selectedItemPhase: true,
            selectedHealthInputs: (capturedHealthWorld, plan.Grappling),
            selectedVoidStorageOpen: plan.VoidStorageOpen) ||
            health is not { SourceProfileKnown: true } knownHealth)
            return false;
        int potionDuration = plan.Buffs.GetLastActiveDuration(new BuffTypeId(21));
        if (potionDuration > 0)
            delay = potionDuration;
        var manaFacts = new PlayerManaRegenerationFacts1458(plan.Vx, plan.Vy, plan.Grappling!.Value, false, ((plan.Appearance?.ConsumableUnlockFlags ?? 0) & 4) != 0, 0, 0f, 0);
        if (!TryConsumeLocalMagicManaStage(plan,ref item,random,out bool skipManaRegeneration))return false;
        var mana=item.Mana;
        if (!skipManaRegeneration && !VanillaRemoteManaRegeneration1458.TryStep(item.Mana with { Maximum = (int)Math.Min((long)item.BaseManaMaximum + manaMaximumBonus,
            400) },
            in manaFacts,
            out mana,
            out _))
            return false;
        mana = mana with
        {
            Mana = Math.Min(mana.Mana, mana.Maximum),
            Count = Math.Max(0, mana.Count)
        };
        if(!skipManaRegeneration)AdvanceLocalManaFeedback(ref item,in selected,item.Mana,in mana,random);
        bool left = (member.ControlFlags & 4) != 0;
        bool right = (member.ControlFlags & 8) != 0;
        var horizontal = ResolveRemoteHorizontalIntent(left, right, plan.Vx);
        var jump = (member.ControlFlags & 16) != 0 ? ServerPlayerJumpIntent.Held : ServerPlayerJumpIntent.Released;
        var source = plan.Before;
        var previousJump = physical.Jump;
        var contacts = physical.Contacts;
        // Food contributes moveSpeed before the ordinary source horizontal multiplier.
        // It changes maxRunSpeed and acceleration, not accRunSpeed or slowdown.
        var foodHorizontal = new VanillaServerPlayerHorizontalProfile1458(VanillaServerPlayerHorizontalControl.MaximumRunSpeed * buffCombat.MoveSpeed,
            VanillaServerPlayerHorizontalControl.MaximumRunSpeed,
            VanillaServerPlayerHorizontalControl.RunAcceleration * buffCombat.MoveSpeed,
            VanillaServerPlayerHorizontalControl.RunSlowdown,
            false);
        if (!physics.TryStep(in source,
            horizontal,
            jump,
            in previousJump,
            in contacts,
            in world,
            in foodHorizontal,
            out var motion,
            out var nextJump) ||
            motion.LiquidContacts != default)
            return false;
        plan.X = motion.PositionX;
        plan.Y = motion.PositionY;
        plan.Vx = motion.VelocityX;
        plan.Vy = motion.VelocityY;
        // Player.Update (1.4.5.8) clears wingTime when wingsLogic is zero; the generic
        // jump helper's grounded recharge belongs to its separate wing-capable callers.
        plan.Physics = new(nextJump with { WingTime = 0 }, motion.LiquidContacts);
        var facts = new PlayerSelectedConsumableFacts1458(selected.ItemType,
            knownHealth.Life,
            maximum,
            mana.Mana,
            mana.Maximum,
            (member.ControlFlags & 32) != 0,
            (member.MiscFlags2 & 64) != 0,
            plan.Buffs.HasNoItemsEffect,
            false,
            true,
            false);
        var clocks = item.Selected with
        {
            PotionDelay = delay
        };
        PlayerSelectedConsumableTransition1458 use;
        bool beganActualUse;
        bool mining = false;
        bool ownedConsumableClock = false;
        bool trackedLocalPlacement = false;
        bool trackedLocalWiring = false;
        VanillaUnifiedRandom1458? placementBeforeCommon = null;
        int placementCrit = -1;
        ToolFrameFacts1458 placementCommonFacts = default;
        if (toolProjectiles is not null && FishingRules1458.TryGetPole(selected.ItemType.Value, out _))
        {
            if (!TryPlanFishingItemCheck(plan, in selected, in facts, in clocks, ref item, random, out use))
                return false;
            trackedLocalPlacement = true; // Already advanced the common local-clock owner.
            beganActualUse = false;
        }
        else if (toolWorld is not null && VanillaToolCatalog1458.TryGet(selected.ItemType, out _))
        {
            if (item.ToolUse is not { } tools || item.ToolTime is not { } remoteToolTime ||
                !VanillaToolCatalog1458.TryResolve(selected.ItemType, selected.Prefix,
                    world.RemixWorld, world.WindowsItemPrefixArithmetic, out var tool))
                return false;
            bool sourceAllowed = true;
            if (tool.Shoot == 1052)
            {
                if (remotePlayerProjectiles is not { } projectileOwner ||
                    !projectileOwner.TryPrepareSourceOwnerTypeQuery(member.Slot.Value,
                        new(1052), out var query) || query is null) return false;
                plan.ToolSourceQuery = query;
                sourceAllowed = !query.Contains;
            }
            var toolFacts = new ToolFrameFacts1458(true, facts.ControlUseItem,
                facts.LastUseSuccess, facts.Cursed, facts.CrowdControlled,
                facts.SelectionBuffered, false,
                tool.NoMeleeSpeedBonus ? 1f : derivedCombat.MeleeAnimationMultiplier)
            { SourceUseAllowed = sourceAllowed, WindowsArithmetic = world.WindowsItemPrefixArithmetic };
            var sourceToolPose = new RuntimePlayerToolStagePose1458(plan.Before with
            {
                PositionX = plan.X, PositionY = plan.Y,
                VelocityX = plan.Vx, VelocityY = plan.Vy
            }, (int)VanillaBasePlayerWidth, (int)VanillaBasePlayerHeight, physical.GravityDirection);
            if (!TryResolveToolReach(in world, in tool, plan.Equipment, out var toolReach)) return false;
            var remoteToolClocks = new ToolClockState1458(clocks, remoteToolTime, false, item.Channel);
            int critRoll = random.Next(3);
            if (!TryAdvanceToolFrame(in tool, tools, in remoteToolClocks, in toolFacts,
                in sourceToolPose, currentCombatTick, critRoll, out var toolAfter, out var remoteTool))
                return false;
            toolAfter = toolAfter with { Reach = toolReach };
            var releasedClockAlternative = CaptureReleasedBufferedToolClock(tool, tools, toolAfter,
                toolFacts, critRoll, member, plan.Buffs, world, random, remoteTool.State.Selected.RevolverCritBonus);
            if (!facts.ControlUseItem && toolAfter.DeferredSelectionSlot is null &&
                toolAfter.BuildingPermission is { IsCurrent: true, NoBuilding: false } releasedBuilding)
                toolAfter = toolAfter with
                {
                    ReleaseWitness = new(member.Connection, sourceToolPose, toolReach,
                        releasedBuilding, tool, member.SelectedItem, currentCombatTick)
                        { BufferedClockAlternative = releasedClockAlternative }
                };
            item = item with
            {
                ToolUse = toolAfter,
                ToolTime = remoteTool.State.ToolTime,
                Channel = remoteTool.State.Channel
            };
            use = new(remoteTool.State.Selected, facts.Life, facts.Mana, false, 0, 0)
            {
                PendingItemReuse = remoteTool.PendingItemReuse,
                ResetItemRotation = remoteTool.AttemptedUse && tool.Shoot == 0
            };
            // These common retained fields belong to the dedicated remote
            // ItemCheck. Local selection may start a separate owned tool epoch
            // while the remote animation is still active.
            beganActualUse = remoteTool.BeganActualUse;
        }
        else if (localWiringContext && VanillaWiringToolCatalog1458.TryGet(selected.ItemType, out var wiringDefinition))
        {
            if (item.ToolUse is not { } wiringBefore || item.ToolTime is not { } remoteTime) return false;
            // Default5/3 Simple reach plus the item's boost. Unrepresented range equipment and
            // permanent builder extension do not become a guessed blockRange here.
            foreach (var equipped in plan.Equipment)
            {
                int armorIndex = equipped.SlotId - VanillaPlayerItemSlotCatalog.ArmorStart;
                if (equipped.Stack > 0 && armorIndex is >= 3 and <= 9 &&
                    equipped.ItemNetId is 2215 or 3061 or 5126) return false;
            }
            var wiringFacts = new ToolFrameFacts1458(true, facts.ControlUseItem, facts.LastUseSuccess,
                facts.Cursed, facts.CrowdControlled, facts.SelectionBuffered, false, 1f)
                { WindowsArithmetic = world.WindowsItemPrefixArithmetic };
            var wiringPose = new RuntimePlayerToolStagePose1458(plan.Before with
            {
                PositionX = plan.X, PositionY = plan.Y, VelocityX = plan.Vx, VelocityY = plan.Vy
            }, (int)VanillaBasePlayerWidth, (int)VanillaBasePlayerHeight, physical.GravityDirection);
            var remoteClocks = new ToolClockState1458(clocks, remoteTime, false, item.Channel);
            int critRoll = random.Next(3);
            if (!TryAdvanceWiringFrame(wiringDefinition, wiringBefore, remoteClocks, wiringFacts,
                wiringPose, currentCombatTick, critRoll, out var wiringAfter, out var remoteWiring)) return false;
            trackedLocalWiring = true;
            wiringAfter = wiringAfter with { Reach = new(5, 3, wiringDefinition.TileBoost) };
            item = item with { ToolUse = wiringAfter, ToolTime = remoteWiring.State.ToolTime,
                Channel = remoteWiring.State.Channel };
            use = new(remoteWiring.State.Selected, facts.Life, facts.Mana, false, 0, 0)
                { PendingItemReuse = remoteWiring.PendingItemReuse, ResetItemRotation = remoteWiring.AttemptedUse };
            beganActualUse = remoteWiring.BeganActualUse;
        }
        else if (localPlacementContext &&
            VanillaLocalPlacementCatalog1458.TryGet(selected.ItemType, out var placementDefinition))
        {
            if (item.ToolUse is not { } placementBefore || item.ToolTime is not { } remoteTime ||
                world.JourneyMode is not false) return false;
            // Captured owned permanent boosters use the same source caps as
            // Update; placementDefinition does not borrow mining's PickSpeed or Artisan range.
            float speed = placementDefinition.IsWall ? item.PermanentSpeeds?.WallAnimationMultiplier ?? 1f :
                item.PermanentSpeeds?.TileAnimationMultiplier ?? 1f;
            var placementFacts = new ToolFrameFacts1458(true, facts.ControlUseItem,
                facts.LastUseSuccess, facts.Cursed, facts.CrowdControlled, false, false, speed)
                { WindowsArithmetic = world.WindowsItemPrefixArithmetic };
            var sourcePose = new RuntimePlayerToolStagePose1458(plan.Before with
            {
                PositionX = plan.X, PositionY = plan.Y, VelocityX = plan.Vx, VelocityY = plan.Vy
            }, (int)VanillaBasePlayerWidth, (int)VanillaBasePlayerHeight, physical.GravityDirection);
            var remoteClocks = new ToolClockState1458(clocks, remoteTime, false, item.Channel);
            placementBeforeCommon = random.Clone();
            int critRoll = random.Next(3);
            placementCrit = critRoll;
            placementCommonFacts = placementFacts;
            if (!TryAdvancePlacementFrame(placementDefinition, placementBefore, remoteClocks, placementFacts,
                sourcePose, speed, currentCombatTick, critRoll, out var placementAfter, out var remotePlacement))
                return false;
            trackedLocalPlacement = true;
            placementAfter = placementAfter with { Reach = new(5, 3, placementDefinition.TileBoost) };
            item = item with { ToolUse = placementAfter, ToolTime = remotePlacement.State.ToolTime,
                Channel = remotePlacement.State.Channel };
            use = new(remotePlacement.State.Selected, facts.Life, facts.Mana, false, 0, 0)
            { PendingItemReuse = remotePlacement.PendingItemReuse,
              ResetItemRotation = remotePlacement.AttemptedUse };
            beganActualUse = remotePlacement.BeganActualUse;
        }
        else if (VanillaManaCrystalUse1458.IsSupported(selected.ItemType, selected.Prefix))
        {
            if (!TryPlanManaCrystal(plan, selected, item, clocks, in facts, random, out use, out item))
                return false;
            if (plan.PublishMana)
                mana = mana with
                {
                    Maximum = item.Mana.Maximum
                };
            beganActualUse = use.BeganUse;
            ownedConsumableClock = true;
        }
        else if (selected.ItemType.Value == 5043)
        {
            if (!TryPlanOwnedTorchFavor(plan, selected, item, clocks, in facts, random, out use, out item))
                return false;
            beganActualUse = use.BeganUse;
            ownedConsumableClock = true;
        }
        else if (PermanentUpgradeBit(selected.ItemType.Value) != 0)
        {
            if (!TryPlanOwnedPermanentUpgrade(plan, selected, item, clocks, in facts, random, out use, out item))
                return false;
            beganActualUse = use.BeganUse;
            ownedConsumableClock = true;
        }
        else if (VanillaGemStaff1458.TryGetWeapon(selected.ItemType, out _))
        {
            if (item.BaseManaMaximum <= 0 || item.MagicPayment is { } invalid && !invalid.IsValid || item.DerivedCombat is not { } gemCombat)
                return false;
            bool? extra = plan.Appearance is { } gemAppearance ? (gemAppearance.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
            var gemContext = new VanillaPlayerCombatEquipmentContext(extra, combatEquipmentExpertMode, combatEquipmentMasterMode)
            {
                LocalVanityArmor = combatEquipmentLocalVanityArmor
            };
            if (!VanillaPlayerCombatEquipmentCatalog.TryCaptureEffectiveGemStaffBody(plan.Equipment,
                in gemContext,
                out var body) ||
                !VanillaGemStaff1458.TryPackFeatures(selected.ItemType,
                body,
                out var features) ||
                !VanillaGemStaff1458.TryResolveWeapon(selected.ItemType,
                selected.Prefix,
                world.WindowsItemPrefixArithmetic,
                out var gemWeapon,
                out _))
                return false;
            var previous = item.MagicPayment;
            bool same = previous is { GemStaff: true } old &&
                old.Matches(member.Connection.Player,
                member.SelectedItem,
                selected,
                item.BaseManaMaximum) &&
                old.EffectiveBody == body;
            var localClocks = same ? previous!.Value.GemClocks : clocks;
            bool ownedGemPreimage=same;
            bool deferredGem=false;
            if(toolWorld is not null && item.ToolUse is {LocalClocksKnown:true} gemShadow)
            {
                ownedGemPreimage=true;
                if(!same)localClocks=gemShadow.Clocks.Selected;
                bool changed=!gemShadow.SelectionInitialized || gemShadow.SourceSlot!=member.SelectedItem ||
                    gemShadow.SourceItem!=selected.ItemType || gemShadow.SourcePrefix!=selected.Prefix;
                bool ready=localClocks.Animation==0 && localClocks.ItemTime==0 && gemShadow.Clocks.ReuseDelay==0;
                deferredGem=changed && !ready;
                if(changed && ready)localClocks=localClocks with {ItemTime=0,ItemTimeMax=0,
                    ReleaseUseItem=facts.ControlUseItem || localClocks.ReleaseUseItem};
            }
            bool prepaid = same && previous is { AwaitingPhase: true };
            int available = mana.Mana;
            RuntimePlayerMagicRefill1458? refill = null;
            bool localAttempt = facts.ControlUseItem &&
                !facts.Cursed &&
                !facts.CrowdControlled &&
                !facts.SelectionBuffered &&
                !deferredGem &&
                (localClocks.ReleaseUseItem &&
                localClocks.Animation == 0 ||
                gemWeapon.AutoReuse &&
                localClocks.Animation <= 1);
            if (localAttempt && !prepaid && gemCombat.ManaFlower && available < (int)(gemWeapon.ManaCost * gemCombat.ManaCostMultiplier))
            {
                if (previous is { Attempt: ulong.MaxValue } ||
                    !TryStageAutoQuickMana(member.Connection,
                    (previous?.Attempt ?? 0) + 1,
                    available,
                    mana.Maximum,
                    item.ManaPotionDelay,
                    plan.InventoryBefore,
                    plan.Buffs,
                    out available,
                    out refill))
                    return false;
                if (refill is { } restored)
                {
                    plan.InventoryMutations.Add(new(member.Connection, restored.Slot, restored.Before, restored.After) { ConsumeFundedQuantity = true });
                    plan.PublishMana = plan.PublishBuffs = true;
                }
            }

            var gemFacts = new PlayerGemStaffFacts1458(selected.ItemType,
                selected.Prefix,
                body,
                available,
                item.SlowMagicUse,
                facts.ControlUseItem,
                same ? previous!.Value.GemLastUseSuccess : facts.LastUseSuccess,
                facts.Cursed,
                facts.CrowdControlled,
                facts.SelectionBuffered || deferredGem,
                true)
            {
                Prepaid = prepaid,
                PrepaidSlowMagicUse = prepaid && previous!.Value.SlowMagicUse,
                ManaCostMultiplier = gemCombat.ManaCostMultiplier,
                WindowsItemPrefixArithmetic = world.WindowsItemPrefixArithmetic
            };
            // Both caller roles observe the same single source crit draw; the detached
            // local clock authorizes payment/volley, while visible clocks remain dedicated remote.
            int? critDraw = null;
            int SharedCrit(int min, int max) => critDraw ??= random.Next(min, max);
            if (!VanillaGemStaffUse1458.TryStep(localClocks, in gemFacts, SharedCrit, out var localUse))
                return false;
            if(ownedGemPreimage && !deferredGem)plan.LocalGemFrame=localUse;
            var remoteFacts = gemFacts with
            {
                LocalPlayer = false,
                Mana = localUse.Mana,
                Prepaid = true,
                PrepaidSlowMagicUse = localUse.SlowMagicUse
            };
            if (!VanillaGemStaffUse1458.TryStep(clocks, in remoteFacts, SharedCrit, out var remoteUse))
                return false;
            if (localUse.BeganActualUse)
            {
                if (previous is { Attempt: ulong.MaxValue })
                    return false;
                item = item with
                {
                    MagicPayment = new(member.Connection.Player,
                        prepaid ? previous!.Value.Attempt : (previous?.Attempt ?? 0) + 1,
                        member.SelectedItem,
                        selected,
                        item.BaseManaMaximum,
                        localUse.Mana,
                        localUse.SlowMagicUse,
                        localUse.InitialShoot,
                        prepaid,
                        false)
                    {
                        GemStaff = true,
                        GemClocks = localUse.State,
                        GemLastUseSuccess = localUse.LastUseSuccess,
                        EffectiveBody = body,
                        GemFeatures = features,
                        Refill = refill ?? (prepaid ? previous!.Value.Refill : null)
                    }
                };
            }
            else if (same)
                item = item with
                {
                    MagicPayment = previous!.Value with
                    {
                        GemClocks = localUse.State,
                        GemLastUseSuccess = localUse.LastUseSuccess,
                        AwaitingPhase = false,
                        MidpointEligible = previous.Value.MidpointEligible || localUse.MidpointShoot,
                        EligibleBirth = previous.Value.EligibleBirth || localUse.InitialShoot && !previous.Value.BirthConsumed
                    }
                };
            else if (previous is { } stale)
                item = item with
                {
                    MagicPayment = stale with
                    {
                        EligibleBirth = false,
                        MidpointEligible = false
                    }
                };
            item = item with
            {
                Channel = false,
                SlowMagicUse = localUse.SlowMagicUse
            };
            if (remoteUse.RefreshManaDelay)
                mana = mana with
                {
                    Delay = 4f
                };
            use = new(remoteUse.State, facts.Life, localUse.Mana, false, 0, 0)
            {
                PendingItemReuse = remoteUse.PendingItemReuse
            };
            beganActualUse = localUse.BeganActualUse;
        }
        else if (VanillaRemoteChannelledMagic1458.IsSupported(selected.ItemType, selected.Prefix, world.WindowsItemPrefixArithmetic))
        {
            var priorPayment = item.MagicPayment;
            var localMagicClocks=clocks;
            bool localMagicDeferred=false;
            if(toolWorld is not null && item.ToolUse is {LocalClocksKnown:true} localShadow)
            {
                // A paid packet-first frame owns its pre-Shoot source image.
                // Unknown local custody leaves the independent remote phase
                // alive, but can never reconstruct local tool permission.
                localMagicClocks=localShadow.WeaponShoot is {Committed:true,Frame:null} paidClock
                    ? paidClock.Before.Selected : localShadow.Clocks.Selected;
                bool changed=!localShadow.SelectionInitialized || localShadow.SourceSlot!=member.SelectedItem ||
                    localShadow.SourceItem!=selected.ItemType || localShadow.SourcePrefix!=selected.Prefix;
                bool ready=localMagicClocks.Animation==0 && localMagicClocks.ItemTime==0 && localShadow.Clocks.ReuseDelay==0;
                localMagicDeferred=changed && !ready;
                if(changed && ready)localMagicClocks=localMagicClocks with
                    {ItemTime=0,ItemTimeMax=0,ReleaseUseItem=facts.ControlUseItem || localMagicClocks.ReleaseUseItem};
            }
            bool prepaid = priorPayment is { AwaitingPhase: true } paid && paid.Matches(member.Connection.Player, member.SelectedItem, selected, item.BaseManaMaximum);
            RuntimePlayerMagicRefill1458? refill = null;
            int available = mana.Mana;
            bool attempt = facts.ControlUseItem && localMagicClocks.ReleaseUseItem && localMagicClocks.Animation == 0 && !facts.Cursed && !facts.CrowdControlled && !facts.SelectionBuffered && !localMagicDeferred;
            if (!VanillaChanneledMagicWeaponStats1458.TryResolve(selected.ItemType,
                selected.Prefix,
                world.WindowsItemPrefixArithmetic,
                out var magicWeapon,
                out _) ||
                item.DerivedCombat is not { } magicCombat ||
                !VanillaRemoteChannelledMagic1458.TryResolveManaCost(magicWeapon.ManaCost,
                magicCombat.ManaCostMultiplier,
                out int cost))
                return false;
            if (attempt && !prepaid && magicCombat.ManaFlower && available < cost)
            {
                if (priorPayment is { Attempt: ulong.MaxValue } ||
                    !TryStageAutoQuickMana(member.Connection,
                    (priorPayment?.Attempt ?? 0) + 1,
                    available,
                    mana.Maximum,
                    item.ManaPotionDelay,
                    plan.InventoryBefore,
                    plan.Buffs,
                    out available,
                    out refill))
                    return false;
                if (refill is { } restored)
                {
                    plan.InventoryMutations.Add(new(member.Connection, restored.Slot, restored.Before, restored.After) { ConsumeFundedQuantity = true });
                    plan.PublishMana = true;
                    plan.PublishBuffs = true;
                }
            }

            var magicFacts = new PlayerRemoteChannelledMagicFacts1458(selected.ItemType,
                selected.Prefix,
                available,
                item.Channel,
                item.SlowMagicUse,
                facts.ControlUseItem,
                facts.LastUseSuccess,
                facts.Cursed,
                facts.CrowdControlled,
                facts.SelectionBuffered || localMagicDeferred,
                prepaid,
                prepaid &&
                priorPayment!.Value.SlowMagicUse,
                magicCombat.ManaCostMultiplier,
                world.WindowsItemPrefixArithmetic);
            // Source local payment/animation and dedicated presentation share
            // one consumed common sample, never the remote renewal preimage.
            int? magicCrit=null;
            int SharedMagicCrit(int min,int max)=>magicCrit??=random.Next(min,max);
            if (!VanillaRemoteChannelledMagic1458.TryStep(localMagicClocks, in magicFacts, SharedMagicCrit, out var magicUse))
                return false;
            var remoteMagicFacts=magicFacts with {Mana=magicUse.Mana,Prepaid=true,PrepaidSlowMagicUse=magicUse.SlowMagicUse};
            if(!VanillaRemoteChannelledMagic1458.TryStep(clocks,in remoteMagicFacts,SharedMagicCrit,out var remoteMagicUse))return false;
            if (magicUse.AttemptedUse)
            {
                if (prepaid)
                    item = item with
                    {
                        MagicPayment = priorPayment!.Value with
                        {
                            AwaitingPhase = false
                        }
                    };
                else
                {
                    if (priorPayment is { Attempt: ulong.MaxValue })
                        return false;
                    item = item with
                    {
                        MagicPayment = new(member.Connection.Player,
                            (priorPayment?.Attempt ?? 0) + 1,
                            member.SelectedItem,
                            selected,
                            item.BaseManaMaximum,
                            magicUse.Mana,
                            magicUse.SlowMagicUse,
                            magicUse.BeganActualUse,
                            false,
                            false)
                        {
                            Refill = refill
                        }
                    };
                }
            }
            else if (priorPayment is { } stale && (!facts.ControlUseItem || !stale.Matches(member.Connection.Player, member.SelectedItem, selected, item.BaseManaMaximum)))
                item = item with
                {
                    MagicPayment = stale with
                    {
                        EligibleBirth = false,
                        AwaitingPhase = false
                    }
                };
            if (magicUse.BeganActualUse)
                item = item with
                {
                    OwnedChannelExpectedType = magicWeapon.ProjectileType.Value
                };
            item = item with
            {
                Channel = magicUse.Channel,
                SlowMagicUse = magicUse.SlowMagicUse
            };
            if (magicUse.RefreshManaDelay)
                mana = mana with
                {
                    Delay = 4f
                };
            use = new(remoteMagicUse.State, facts.Life, magicUse.Mana, false, 0, 0)
            {
                PendingItemReuse = remoteMagicUse.PendingItemReuse
            };
            beganActualUse = magicUse.BeganActualUse;
        }
        else if (VanillaRemoteRangedItemCheck1458.TryGetAmmoFamily(selected.ItemType, out var ammoFamily))
        {
            var rangedFacts = new PlayerRemoteRangedItemFacts1458(selected.ItemType,
                CaptureRemoteRangedAmmo(member,
                ammoFamily),
                facts.ControlUseItem,
                facts.LastUseSuccess,
                facts.Cursed,
                facts.CrowdControlled,
                facts.SelectionBuffered,
                selected.Prefix,
                world.WindowsItemPrefixArithmetic);
            if (!VanillaRemoteRangedItemCheck1458.TryStep(clocks, in rangedFacts, random.Next, out var rangedUse))
                return false;
            use = new(rangedUse.State, facts.Life, facts.Mana, false, 0, 0)
            {
                PendingItemReuse = rangedUse.PendingItemReuse
            };
            beganActualUse = rangedUse.BeganActualUse;
        }
        else if (VanillaRemoteMeleeItemCheck1458.IsSupported(selected.ItemType, selected.Prefix, world.WindowsItemPrefixArithmetic))
        {
            var meleeFacts = new PlayerRemoteMeleeItemFacts1458(selected.ItemType,
                facts.ControlUseItem,
                facts.LastUseSuccess,
                facts.Cursed,
                facts.CrowdControlled,
                facts.SelectionBuffered,
                selected.Prefix,
                world.WindowsItemPrefixArithmetic);
            if (!VanillaRemoteMeleeItemCheck1458.TryStep(clocks, in meleeFacts, random.Next, out var meleeUse, derivedCombat.MeleeAnimationMultiplier))
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
            var passiveFacts = new PlayerRemotePassiveItemFacts1458(selected.ItemType,
                facts.ControlUseItem,
                facts.LastUseSuccess,
                facts.Cursed,
                facts.CrowdControlled,
                facts.SelectionBuffered,
                selected.Prefix);
            float placementAnimationMultiplier = 1f;
            if (item.PermanentSpeeds is { } placementSpeeds && VanillaItemPlacementTable1458.TryGet(selected.ItemType, out var placement))
            {
                // ApplyItemAnimation reads capped/inverted tile speed first,
                // otherwise wall speed. Mining pickSpeed does not alter this clock.
                if (placement.PlacesTile)
                    placementAnimationMultiplier = placementSpeeds.TileAnimationMultiplier;
                else if (placement.PlacesWall)
                    placementAnimationMultiplier = placementSpeeds.WallAnimationMultiplier;
            }

            if (!VanillaRemotePassiveItemCheck1458.TryStep(clocks, in passiveFacts, random.Next, out var passiveUse, placementAnimationMultiplier))
                return false;
            use = new(passiveUse.State, facts.Life, facts.Mana, false, 0, 0)
            {
                PendingItemReuse = passiveUse.PendingItemReuse,
                ResetItemRotation = passiveUse.ResetItemRotation
            };
            beganActualUse = passiveUse.BeganActualUse;
        }
        else if (VanillaSelectedConsumableCatalog1458.TryGet(selected.ItemType,
            out var selectedPotion) &&
            (selectedPotion.HealLife > 0 ||
            selectedPotion.HealMana > 0 ||
            selectedPotion.BuffType > 0 ||
            selected.ItemType.Value is 29 or 1291))
        {
            if (!TryPlanOwnedSelectedPotion(plan, selected, item, clocks, in facts, random, out use, out item))
                return false;
            beganActualUse = use.BeganUse;
            ownedConsumableClock = true;
        }
        else
        {
            if (!VanillaSelectedConsumable1458.TryStep(clocks, in facts, random.Next, random.NextDouble, out use))
                return false;
            beganActualUse = use.BeganUse;
        }

        // ItemCheck clears non-shooting item rotation when attempting use, even if
        // the later success/cursed/potion-delay gate rejects the actual start.
        if (use.ResetItemRotation)
            plan.Rotation = 0f;
        if (use.PotionSicknessOffer > 0)
            plan.Buffs.TryApplySelectedConsumable(new BuffTypeId(21), use.PotionSicknessOffer, false);
        if (use.ManaSicknessOffer > 0)
            plan.Buffs.TryApplySelectedConsumable(new BuffTypeId(94), use.ManaSicknessOffer, false);
        if (use.BuffDurationOffer > 0)
            plan.Buffs.TryApplySelectedConsumable(use.BuffOfferType, use.BuffDurationOffer, false);
        if (use.Consumed && use.BuffOfferType.Value is 26 or 206 or 207 or 121 or 122 or 123)
            plan.Buffs.StampOwnedFoodGrant(currentCombatTick);
        if (!VanillaToolCatalog1458.TryGet(selected.ItemType, out _) &&
            !trackedLocalPlacement && !trackedLocalWiring && item.ToolUse is { } nonToolShadow)
        {
            var shadow = TrackNonToolLocalClock(nonToolShadow, member.SelectedItem,
                in selected, in facts, in clocks, in use, item.MagicPayment, ownedConsumableClock, in item, plan);
            // A metadata-only remote step cannot retain a prior local placement
            // explanation or make old reports eligible after a later context bind.
            item = item with { ToolUse = shadow with { SourcePlacement = null,
                PlacementReady = null, PlacementAlternative = null, PlacementHistoryEpoch = 0 } };
        }
        else if (toolWorld is null && item.ToolUse is { } unboundTool)
            // A standalone remote ItemCheck projection owns no local cursor or
            // world action. Keep its established remote clock behavior without
            // retaining constructor-zero local clocks for a future world bind.
            item = item with { ToolUse = unboundTool with
            {
                LocalClocksKnown = false, SourceStagePose = null, Reach = null,
                BuildingPermission = null, ReleaseWitness = null,
                RequiresFreshUse = true, ShootEligible = false, ToolStagePending = false
            } };
        plan.Health = knownHealth with
        {
            Life = use.Life
        };
        plan.Item = item with
        {
            Mana = mana with
            {
                Mana = use.Mana
            },
            Selected = use.State,
            PendingItemReuse = use.PendingItemReuse,
            AttackCD = beganActualUse ? 0 : item.AttackCD,
            ToolTime = beganActualUse && mining ? 1 : item.ToolTime
        };
        if (placementBeforeCommon is not null)
            AdvancePlacementAlternative(plan, world, placementBeforeCommon, random, placementCrit, placementCommonFacts);
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

            if (item.Stack <= 0)
                continue;
            if (!VanillaItemIds.TryCreate(item.ItemType.Value, out _))
                unknown = true;
            else if (VanillaProjectileWeaponCombatCatalog.IsAmmoType(family, item.ItemType))
                return true;
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

    private sealed class RemoteItemPhasePlan1458(RuntimePlayerMember member,
        PlayerStateSnapshot before,
        RuntimePlayerItemPhase1458? item,
        RuntimePlayerPhysicsPhase1458? physics,
        PlayerAppearanceCommitRequest? appearance,
        PlayerEquipmentCommitRequest[] equipment,
        BuffTypeId[] buffTypes,
        PlayerBuffState buffs,
        bool godMode,
        bool ownsManaMaximum,
        bool ownsLifeMaximum)
    {
        internal FishingBobber1458? FishingBefore;
        internal TerraRuntime.Core.Projectiles.RuntimeProjectileStore.SourceOwnerTypeQueryPreparation? ToolSourceQuery;
        internal RuntimePlayerToolInput1458? BeforeToolInput { get; } = item?.ToolUse?.Input;
        internal RuntimePlayerToolActorCapture1458? BeforeToolInputActor { get; } = item?.ToolUse?.Input?.Actor;
        internal ulong BeforeToolInputRevision { get; } = item?.ToolUse?.Input?.Revision ?? 0;
        internal RuntimePlayerPlacementAlternative1458? PlacementChosenAlternative { get; set; }
        internal VanillaUnifiedRandom1458? SourceRandom { get; set; }
        internal RuntimePlayerInventoryStore.ShopJournalCapture? AmmoInventoryBefore { get; set; }
        internal AmmoCyclingState1458? AmmoCyclingAfterShoot { get; set; }
        internal RuntimePlayerAmmoShootHistory1458? RefreshedAmmoHistory { get; set; }
        internal bool VoidStorageOpen { get; set; }
        internal RuntimeTownShopSession1458? BeforeShopSession { get; set; }
        internal RuntimePlayerInventoryItem[] InventoryBefore { get; } = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        internal List<RemotePhaseInventoryMutation> InventoryMutations { get; } = [];
        internal bool PublishMana { get; set; }
        internal bool PublishHealth { get; set; }
        internal bool PublishBuffs { get; set; }
        internal TerraRuntime.Contracts.Gameplay.BuffTypeId[]? PublishedBuffTypes { get; set; }
        internal RuntimePlayerMember Member { get; } = member;
        internal RuntimeItemUseCaller1458 BeforeItemUseCaller { get; } = member.ItemUseCaller;
        internal PlayerStateSnapshot Before { get; } = before;
        internal RuntimePlayerItemPhase1458? BeforeItem { get; } = item;
        internal PlayerGemStaffTransition1458? LocalGemFrame { get; set; }
        internal RuntimePlayerPhysicsPhase1458? BeforePhysics { get; } = physics;
        internal PlayerAppearanceCommitRequest? Appearance { get; } = appearance;
        internal PlayerEquipmentCommitRequest[] Equipment { get; } = equipment;
        internal BuffTypeId[] BuffTypes { get; } = buffTypes;
        internal PlayerBuffState BeforeBuffs { get; } = buffs;
        internal bool GodMode { get; } = godMode;
        internal bool BeforeOwnsManaMaximum { get; } = ownsManaMaximum;
        internal bool BeforeOwnsLifeMaximum { get; } = ownsLifeMaximum;
        internal byte BeforePermanentUpgradeMask { get; set; }
        internal bool BeforeOwnsTorchFavor { get; set; }
        internal bool BeforePaidTorchFavor { get; set; }
        internal bool CreditTorchFavor { get; set; }
        internal PlayerAppearanceCommitRequest? AcceptedPermanentAppearance { get; set; }
        internal int? AcceptedBaseLifeMaximum { get; set; }
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
