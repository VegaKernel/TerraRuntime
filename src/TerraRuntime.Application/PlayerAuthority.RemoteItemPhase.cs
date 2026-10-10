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
        var plans = new RemoteItemPhasePlan1458[members.Length];
        // Capture the entire census before the health world/grapple providers can run.
        for (int i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!transferProfiles.TryCapture(member.Connection, out var appearance, out var equipment, out var buffs) ||
                buffs is null || transferProfiles.CaptureBuffState(member.Connection) is not { } buffState)
                return false;
            plans[i] = new(member, member.CaptureSnapshot(), member.ItemPhase, member.PhysicsPhase,
                appearance, equipment, buffs, buffState, member.GodMode);
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

        // No external readers occur after this tail. Adoption cannot expose a partial census.
        if (membership.Serial != memberSerial || inventory.Serial != inventorySerial ||
            !playerUpdateRandomOwned || !ReferenceEquals(playerUpdateRandom, random) ||
            !ReferenceEquals(npcHealthWorldFacts, healthWorldSource) ||
            !ReferenceEquals(npcHealthGrapplingFacts, grapplingSource) ||
            remotePlayerHealthWorld != ownedHealthWorld || !ReferenceEquals(remotePlayerProjectiles, ownedProjectiles) ||
            !random.HasSameState(beforeRandom) || playerUpdateWorld != world ||
            serverPlayers?.CopySnapshots(bots) > 0) return false;
        for (int i = 0; i < versions.Length; i++)
            if (tiles.GetSectionVersion(TerrariaSectionGeometry.FromLinearIndex(tiles.Dimensions, i)) != versions[i])
                return false;
        foreach (var plan in plans)
        {
            var member = plan.Member;
            if ((plan.HealthWorld is not null && CaptureRemotePlayerGrappling(plan.Before.Player) != plan.Grappling) ||
                !membership.TryGet(member.Connection, out var current) || !ReferenceEquals(current, member) ||
                member.CaptureSnapshot() != plan.Before || member.ItemPhase != plan.BeforeItem ||
                member.PhysicsPhase != plan.BeforePhysics || member.GodMode != plan.GodMode ||
                member.Revision == ulong.MaxValue ||
                !transferProfiles.TryCapture(member.Connection, out var appearance, out var equipment, out var buffs) ||
                appearance != plan.Appearance || !equipment.AsSpan().SequenceEqual(plan.Equipment) ||
                buffs is null || !buffs.AsSpan().SequenceEqual(plan.BuffTypes) ||
                !transferProfiles.IsCurrentBuffState(member.Connection, plan.BeforeBuffs)) return false;
        }
        foreach (var plan in plans)
        {
            if (!plan.Changed) continue;
            var member = plan.Member;
            // Every condition was checked above; these owned assignments invoke no callbacks.
            transferProfiles.TryAdoptBuffState(member.Connection, plan.BeforeBuffs, plan.Buffs);
            member.TryAdvanceRemotePhaseRevision();
            member.ItemPhase = plan.Item;
            member.PhysicsPhase = plan.Physics;
            member.ItemAnimation = plan.Item!.Value.Selected.Animation;
            member.ItemRotation = plan.Rotation;
            member.NpcHealth = plan.Health;
            member.NpcLifeCurrent = plan.Health is not null;
            member.DerivedLifeMax = plan.Maximum;
            member.Debuffs = plan.Debuffs;
            member.Mana = checked((short)plan.Item.Value.Mana.Mana);
            member.PositionX = plan.X;
            member.PositionY = plan.Y;
            member.VelocityX = plan.Vx;
            member.VelocityY = plan.Vy;
            member.MarkRemotePhaseSnapshot();
        }
        random.CopyStateFrom(plannedRandom);
        return true;
    }

    private bool TryPlanRemoteItemPhase(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerUpdateWorld1458 world, VanillaServerPlayerDryPhysicsStepper physics,
        VanillaUnifiedRandom1458 random)
    {
        var member = plan.Member;
        if (plan.Item is not { } item || plan.Physics is not { } physical ||
            item.DeadTime is < 0 or >= int.MaxValue || item.RespawnTimer < 0 || item.ManaPotionDelay < 0 ||
            member.NpcHealth is not { SourceProfileKnown: true } || member.HasMount || plan.GodMode ||
            (plan.Appearance?.ConsumableUnlockFlags ?? 0) != 0 ||
            !TryCaptureProvenRemoteEquipment(plan.Equipment, plan.Appearance, out var equipmentCombat, out int manaMaximumBonus) ||
            plan.BuffTypes.Any(static type => type.Value is not (5 or 16 or 21 or 23 or 93 or 94 or 112 or 114 or 115 or 117 or 321)) ||
            member.MiscFlags1 != 0 || (member.MiscFlags2 & ~(1 << 6)) != 0 ||
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
        if (!inventory.TryGet(member.Connection, member.SelectedItem, out var selected) ||
            !selected.IsCanonical ||
            (!(selected.Prefix == VanillaPrefixIds.None &&
                VanillaSelectedConsumableCatalog1458.TryGet(selected.ItemType, out _)) &&
                !VanillaRemoteRangedItemCheck1458.IsSupported(selected.ItemType, selected.Prefix,
                    world.WindowsItemPrefixArithmetic) &&
                !VanillaRemoteMeleeItemCheck1458.IsSupported(selected.ItemType, selected.Prefix,
                    world.WindowsItemPrefixArithmetic) &&
                !VanillaRemotePassiveItemCheck1458.IsSupported(selected.ItemType, selected.Prefix))) return false;
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
        item = item with { DerivedCombat = derivedCombat };
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
        if (VanillaRemoteRangedItemCheck1458.TryGetAmmoFamily(selected.ItemType, out var ammoFamily))
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
            if (!VanillaRemoteMeleeItemCheck1458.TryStep(clocks, in meleeFacts, random.Next, out var meleeUse))
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
        BuffTypeId[] buffTypes, PlayerBuffState buffs, bool godMode)
    {
        internal RuntimePlayerMember Member { get; } = member;
        internal PlayerStateSnapshot Before { get; } = before;
        internal RuntimePlayerItemPhase1458? BeforeItem { get; } = item;
        internal RuntimePlayerPhysicsPhase1458? BeforePhysics { get; } = physics;
        internal PlayerAppearanceCommitRequest? Appearance { get; } = appearance;
        internal PlayerEquipmentCommitRequest[] Equipment { get; } = equipment;
        internal BuffTypeId[] BuffTypes { get; } = buffTypes;
        internal PlayerBuffState BeforeBuffs { get; } = buffs;
        internal bool GodMode { get; } = godMode;
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
