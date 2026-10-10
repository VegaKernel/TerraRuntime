using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private ClientProjectileProvenanceResolveResult TryResolveStrictClientProjectileSpawn(
        ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        bool captured = players.TryCaptureProjectileUse(connection, out var capture);
        var before = projectileRandom.Clone();
        var planned = before.Clone();
        var result = TryResolveStrictClientProjectileSpawnCore(connection, in packet, planned, out authoritative);
        if (result != ClientProjectileProvenanceResolveResult.Accepted) return result;
        if (!captured || capture is null || !players.IsCurrentProjectileUse(capture) ||
            (authoritative.RequiresPlainBowPose && !players.IsCurrentPlainBowProjectilePose(capture)) ||
            !projectileRandom.HasSameState(before))
        {
            authoritative = default;
            return ClientProjectileProvenanceResolveResult.Rejected;
        }
        if (authoritative.AlternativeBulletUse is { } alternative)
            authoritative = authoritative with { AlternativeBulletUse = new(alternative.Use with
                { PlayerCapture = capture, RandomBefore = before }) };
        authoritative = authoritative with { PlayerCapture = capture, RandomBefore = before, RandomAfter = planned };
        return result;
    }

    private ClientProjectileProvenanceResolveResult TryResolveStrictClientProjectileSpawnCore(
        ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet,
        VanillaUnifiedRandom1458 plannedRandom,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        if (!players.TryCapture(connection.Player, out PlayerStateSnapshot player) || player.IsDead)
            return ClientProjectileProvenanceResolveResult.Rejected;

        int selectedSlot = player.SelectedItem;
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot((short)selectedSlot) ||
            !players.TryGetInventoryItem(connection, selectedSlot, out RuntimePlayerInventoryItem weaponItem) ||
            weaponItem.IsEmpty)
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        if (VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(
                weaponItem.ItemType,
                out VanillaChanneledMagicProjectileWeaponCombatDefinition channeledMagicWeapon))
        {
            return TryResolveStrictChanneledMagicProjectileSpawn(
                connection, in player, in weaponItem, in channeledMagicWeapon, in packet, out authoritative);
        }

        if (VanillaProjectileWeaponCombatCatalog.TryGetStandaloneWeapon(
                weaponItem.ItemType,
                out VanillaStandaloneProjectileWeaponCombatDefinition standaloneWeapon))
        {
            return TryResolveStrictStandaloneProjectileSpawn(
                connection, in player, selectedSlot, in weaponItem, in standaloneWeapon, in packet, out authoritative);
        }

        if (!VanillaProjectileWeaponCombatCatalog.TryGetWeapon(
                weaponItem.ItemType,
                out VanillaProjectileWeaponCombatDefinition weapon))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        bool bullet = VanillaBulletWeaponLaunch1458.Supports(weapon.Type);
        bool bow = VanillaOrdinaryBowLaunch1458.Supports(weapon.Type);
        VanillaCombatPrefixModifiers prefix;
        bool sourceBowPrefix = bow && VanillaOrdinaryBowLaunch1458.TryGetPrefixModifiers(weapon.Type,
            weaponItem.Prefix, VanillaBulletSourceArithmetic1458.CoreClrSingle, out _);
        bool supportedPrefix = bow
            ? VanillaOrdinaryBowLaunch1458.TryGetPrefixModifiers(weapon.Type, weaponItem.Prefix,
                VanillaBulletSourceArithmetic1458.CoreClrSingle, out prefix) ||
                VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(weaponItem.Prefix, out prefix)
            : bullet
            ? VanillaBulletWeaponStats1458.TryGetPrefixModifiers(weapon.Type, weaponItem.Prefix,
                VanillaBulletSourceArithmetic1458.CoreClrSingle, out prefix)
            : VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(weaponItem.Prefix, out prefix);
        if (!supportedPrefix ||
            !players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat) ||
            !players.TryCaptureAmmoConservationContext(connection, out bool ammoBox, out bool ammoPotion))
        {
            // An unsupported prefix/equipment combination may still be a legitimate vanilla shot, but it cannot cross
            // the CombatTrusted boundary until its exact source formula is imported.
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        // Expanded bow prefixes are owned by the neutral source launch proof. Other modifier
        // contexts retain the earlier generic catalog component mask instead of gaining trust.
        if (bow && !VanillaOrdinaryBowLaunch1458.HasNeutralLaunchModifiers(in attackerCombat) &&
            !VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(weaponItem.Prefix, out prefix))
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        Span<RuntimePlayerInventoryItem> inventory =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!players.TryCopyInventory(connection, inventory))
            return ClientProjectileProvenanceResolveResult.Rejected;

        int ammoSlot = FindFirstAmmo(
            weapon.AmmoFamily,
            inventory,
            VanillaPlayerItemSlotCatalog.CoinSlotStart,
            VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive,
            out RuntimePlayerInventoryItem ammoItem,
            out VanillaProjectileAmmoCombatDefinition ammo);
        if (ammoSlot == -1)
            ammoSlot = FindFirstAmmo(
                weapon.AmmoFamily,
                inventory,
                VanillaPlayerItemSlotCatalog.AmmoSlotStart,
                VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive,
                out ammoItem,
                out ammo);
        if (ammoSlot == -1)
            ammoSlot = FindFirstAmmo(
                weapon.AmmoFamily,
                inventory,
                VanillaPlayerItemSlotCatalog.MainInventoryStart,
                VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive,
                out ammoItem,
                out ammo);
        if (ammoSlot < 0)
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        if (bow)
        {
            if (ammoItem.ItemType == VanillaItemIds.WoodenArrow ||
                VanillaOrdinaryBowLaunch1458.SupportsAmmo(ammoItem.ItemType) &&
                VanillaOrdinaryBowLaunch1458.HasNeutralLaunchModifiers(in attackerCombat))
            {
                if (!sourceBowPrefix)
                    return ClientProjectileProvenanceResolveResult.NotApplicable;
            }
            else if (!VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(weaponItem.Prefix, out prefix))
                return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        if (!VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out ProjectileTypeId expectedProjectileType))
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        // Projectile 714 is the vanilla client-owned presentation/channel holder. It never receives terrain
        // authority; only its exact child volley can cross the CombatTrusted boundary below.
        if (weapon.Type == VanillaItemIds.CelebrationMk2 &&
            packet.ProjectileType == VanillaProjectileIds.CelebrationMk2HeldProjectile.Value)
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        int expectedDamage = VanillaProjectileWeaponCombatCatalog.ResolveDamage(
            in weapon, in ammo, in prefix, in attackerCombat);
        float expectedKnockBack = VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(
            in weapon, in ammo, in prefix, in attackerCombat);
        VanillaLaunchSpeedEnvelope speedEnvelope = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in prefix, in attackerCombat);
        if (!speedEnvelope.IsValid)
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        float packetSpeedSquared = packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY;
        if (!(packetSpeedSquared > 0f) || !float.IsFinite(packetSpeedSquared))
            return RejectProvenance();
        float packetSpeed = MathF.Sqrt(packetSpeedSquared);

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = packet.PositionX - playerCx;
        float dy = packet.PositionY - playerCy;
        float maximumDistance = weapon.ImpossibleSpawnCenterDistancePixels;
        int authoritativeUseTime = Math.Max(1, (int)Math.Round(weapon.UseTimeTicks * prefix.SpeedMultiplier));
        long tick = tickProvider();
        float knockBackTolerance = MathF.Max(0.001f, MathF.Abs(expectedKnockBack) * 0.00001f);

        if (bow && VanillaOrdinaryBowLaunch1458.SupportsAmmo(ammoItem.ItemType) &&
            VanillaOrdinaryBowLaunch1458.HasNeutralLaunchModifiers(in attackerCombat))
            return TryResolveOrdinaryBowSourceCandidates(connection, in packet, in weaponItem, in weapon,
                in ammo, in ammoItem, in attackerCombat, ammoSlot, ammoBox, ammoPotion, playerCx, playerCy,
                tick, plannedRandom, out authoritative);

        if (weapon.Type == VanillaItemIds.CelebrationMk2)
        {
            return TryResolveStrictCelebrationMk2ChildSpawn(
                connection,
                in packet,
                in expectedProjectileType,
                expectedDamage,
                expectedKnockBack,
                knockBackTolerance,
                maximumDistance,
                tick,
                ammoSlot,
                in ammoItem,
                in weapon,
                in ammo,
                in attackerCombat,
                packetSpeed,
                dx,
                dy,
                plannedRandom,
                ammoBox,
                ammoPotion,
                out authoritative);
        }

        if (bullet)
            return TryResolveBulletSourceCandidates(connection, in packet, in weaponItem, in weapon,
                in ammo, in ammoItem, in attackerCombat, ammoSlot, ammoBox, ammoPotion, dx, dy,
                maximumDistance, tick, plannedRandom, out authoritative);

        bool ordinaryBow = bow && ammoItem.ItemType == VanillaItemIds.WoodenArrow;
        if (ordinaryBow)
        {
            // Source NewProjectile takes the player's MountedCenter; packet27 contains the
            // resulting arrow1 top-left (10x10). The owned plain pose supplies this center.
            // Other ammo/prefix combinations retain the existing catalog component policy.
            if (!VanillaOrdinaryBowLaunch1458.IsValidSpawnCenter(packet.PositionX + 5f, packet.PositionY + 5f,
                    playerCx, playerCy) ||
                !VanillaOrdinaryBowLaunch1458.IsValidVelocity(packet.VelocityX, packet.VelocityY,
                    speedEnvelope.CanonicalMagnitude) || packet.BannerIdToRespondTo != 0 ||
                packet.Ai0 != 0f || packet.Ai1 != 0f || packet.Ai2 != 0f)
                return RejectProvenance();
            if (VanillaOrdinaryBowLaunch1458.TryResolve(weapon.Type, weaponItem.Prefix, ammoItem.ItemType,
                    in attackerCombat, out var sourceLaunch))
            {
                expectedDamage = sourceLaunch.Damage;
                expectedKnockBack = sourceLaunch.KnockBack;
                authoritativeUseTime = sourceLaunch.UseTime;
            }
        }

        if (packet.ProjectileType != expectedProjectileType.Value ||
            packet.Damage != expectedDamage ||
            packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            !speedEnvelope.ContainsMagnitude(packetSpeed) ||
            MathF.Abs(packet.Ai0) > 0.001f ||
            MathF.Abs(packet.Ai1) > 0.001f ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > maximumDistance * maximumDistance ||
            trustedClientUseCadence.IsOnCooldown(connection.Player, tick))
        {
            return RejectProvenance();
        }

        // Preserve the client's aim direction. Only magnitude is validated/canonicalized; there is deliberately no
        // generic angular envelope because source-specific spread belongs to individual weapon rules.
        float canonicalSpeed = speedEnvelope.CanonicalMagnitude;
        float velocityScale = canonicalSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            expectedProjectileType,
            connection.Player.Slot.Value,
            packet.PositionX,
            packet.PositionY,
            ordinaryBow ? packet.VelocityX : packet.VelocityX * velocityScale,
            ordinaryBow ? packet.VelocityY : packet.VelocityY * velocityScale,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)expectedDamage),
            KnockBack: expectedKnockBack,
            OriginalDamage: 0);

        bool conserveAmmo = PrepareAmmoConservation(in weapon, in ammo, in attackerCombat, plannedRandom, ammoBox, ammoPotion);
        RuntimePlayerInventoryItem remainingAmmo = conserveAmmo
            ? ammoItem
            : ammoItem.Stack == 1
                ? default
                : ammoItem with { Stack = checked((short)(ammoItem.Stack - 1)) };
        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            new RuntimePlayerInventoryMutation(checked((short)ammoSlot), remainingAmmo),
            ManaCost: 0,
            speedEnvelope,
            authoritativeUseTime) { UseTick = tick, RequiresPlainBowPose = ordinaryBow };
        return ClientProjectileProvenanceResolveResult.Accepted;

        static ClientProjectileProvenanceResolveResult RejectProvenance() =>
            ClientProjectileProvenanceResolveResult.Rejected;
    }

    private ClientProjectileProvenanceResolveResult TryResolveStrictCelebrationMk2ChildSpawn(
        ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet,
        in ProjectileTypeId expectedProjectileType,
        int expectedDamage,
        float expectedKnockBack,
        float knockBackTolerance,
        float maximumDistance,
        long tick,
        int ammoSlot,
        in RuntimePlayerInventoryItem ammoItem,
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaPlayerCombatSnapshot attackerCombat,
        float packetSpeed,
        float dx,
        float dy,
        VanillaUnifiedRandom1458 plannedRandom,
        bool ammoBox,
        bool ammoPotion,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        int pattern = (int)packet.Ai0;
        float expectedSpeed = VanillaExplosiveProjectileFacts1458.GetCelebrationLaunchSpeed(pattern);
        if (packet.Ai0 != pattern ||
            packet.ProjectileType != expectedProjectileType.Value ||
            packet.Damage != expectedDamage ||
            packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            MathF.Abs(packetSpeed - expectedSpeed) > MathF.Max(0.0005f, expectedSpeed * 0.00001f) ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > maximumDistance * maximumDistance ||
            !celebrationMk2Volleys.TryInspect(
                connection.Player,
                tick,
                pattern,
                packet.Ai1,
                out RuntimeCelebrationMk2VolleyAdmission volley))
        {
            return ClientProjectileProvenanceResolveResult.Rejected;
        }

        float velocityScale = expectedSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            expectedProjectileType,
            connection.Player.Slot.Value,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX * velocityScale,
            packet.VelocityY * velocityScale,
            new ProjectileAiState(pattern, packet.Ai1, 0f),
            BannerIdToRespondTo: 0,
            Damage: checked((short)expectedDamage),
            KnockBack: expectedKnockBack,
            OriginalDamage: 0);

        RuntimePlayerInventoryMutation? inventoryMutation = null;
        if (volley.StartsVolley)
        {
            bool conserveAmmo = PrepareAmmoConservation(in weapon, in ammo, in attackerCombat, plannedRandom, ammoBox, ammoPotion);
            RuntimePlayerInventoryItem remainingAmmo = conserveAmmo
                ? ammoItem
                : ammoItem.Stack == 1
                    ? default
                    : ammoItem with { Stack = checked((short)(ammoItem.Stack - 1)) };
            inventoryMutation = new RuntimePlayerInventoryMutation(checked((short)ammoSlot), remainingAmmo);
        }

        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            inventoryMutation,
            ManaCost: 0,
            new VanillaLaunchSpeedEnvelope(expectedSpeed, expectedSpeed),
            UseTimeTicks: 8,
            CelebrationVolley: volley) { UseTick = tick };
        return ClientProjectileProvenanceResolveResult.Accepted;
    }


    private ClientProjectileProvenanceResolveResult TryResolveStrictChanneledMagicProjectileSpawn(
        ConnectionHandle connection,
        in PlayerStateSnapshot player,
        in RuntimePlayerInventoryItem weaponItem,
        in VanillaChanneledMagicProjectileWeaponCombatDefinition weapon,
        in TerrariaProjectileUpdateState packet,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        // Exact magic prefix formulas are deliberately not guessed in this slice. Prefix-free items plus the
        // source-backed equipment snapshot are enough to make damage/mana/cadence authoritative.
        if (weaponItem.Prefix != VanillaPrefixIds.None || weaponItem.Stack <= 0 ||
            !player.HasMana || player.Mana < weapon.ManaCost ||
            !players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        int expectedDamage = VanillaProjectileWeaponCombatCatalog.ResolveChanneledMagicDamage(in weapon, in attackerCombat);
        float expectedKnockBack = weapon.BaseKnockBack;
        VanillaLaunchSpeedEnvelope speedEnvelope =
            VanillaProjectileWeaponCombatCatalog.ResolveChanneledMagicLaunchSpeedEnvelope(in weapon);
        float speedSquared = packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY;
        if (!(speedSquared > 0f) || !float.IsFinite(speedSquared) || !speedEnvelope.IsValid)
            return ClientProjectileProvenanceResolveResult.Rejected;
        float packetSpeed = MathF.Sqrt(speedSquared);

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = packet.PositionX - playerCx;
        float dy = packet.PositionY - playerCy;
        long tick = tickProvider();
        float knockBackTolerance = MathF.Max(0.001f, MathF.Abs(expectedKnockBack) * 0.00001f);
        if (packet.ProjectileType != weapon.ProjectileType.Value ||
            packet.Damage != expectedDamage || packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            !speedEnvelope.ContainsMagnitude(packetSpeed) ||
            MathF.Abs(packet.Ai0) > 0.001f || MathF.Abs(packet.Ai1) > 0.001f || MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > weapon.ImpossibleSpawnCenterDistancePixels * weapon.ImpossibleSpawnCenterDistancePixels ||
            trustedClientUseCadence.IsOnCooldown(connection.Player, tick))
        {
            return ClientProjectileProvenanceResolveResult.Rejected;
        }

        float canonicalSpeed = speedEnvelope.CanonicalMagnitude;
        float scale = canonicalSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            weapon.ProjectileType,
            connection.Player.Slot.Value,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX * scale,
            packet.VelocityY * scale,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)expectedDamage),
            KnockBack: expectedKnockBack,
            OriginalDamage: 0);
        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            InventoryMutation: null,
            ManaCost: weapon.ManaCost,
            speedEnvelope,
            weapon.UseTimeTicks) { UseTick = tick };
        return ClientProjectileProvenanceResolveResult.Accepted;
    }


    private ClientProjectileProvenanceResolveResult TryResolveStrictStandaloneProjectileSpawn(
        ConnectionHandle connection,
        in PlayerStateSnapshot player,
        int selectedSlot,
        in RuntimePlayerInventoryItem weaponItem,
        in VanillaStandaloneProjectileWeaponCombatDefinition weapon,
        in TerrariaProjectileUpdateState packet,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        if (weaponItem.Prefix != VanillaPrefixIds.None ||
            weaponItem.Stack <= 0 ||
            !players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        int expectedDamage = VanillaProjectileWeaponCombatCatalog.ResolveStandaloneDamage(in weapon, in attackerCombat);
        float expectedKnockBack = weapon.BaseKnockBack;
        VanillaLaunchSpeedEnvelope speedEnvelope =
            VanillaProjectileWeaponCombatCatalog.ResolveStandaloneLaunchSpeedEnvelope(in weapon);
        float speedSquared = packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY;
        if (!(speedSquared > 0f) || !float.IsFinite(speedSquared) || !speedEnvelope.IsValid)
            return ClientProjectileProvenanceResolveResult.Rejected;
        float packetSpeed = MathF.Sqrt(speedSquared);

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = packet.PositionX - playerCx;
        float dy = packet.PositionY - playerCy;
        long tick = tickProvider();
        float knockBackTolerance = MathF.Max(0.001f, MathF.Abs(expectedKnockBack) * 0.00001f);
        if (packet.ProjectileType != weapon.ProjectileType.Value ||
            packet.Damage != expectedDamage ||
            packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            !speedEnvelope.ContainsMagnitude(packetSpeed) ||
            MathF.Abs(packet.Ai0) > 0.001f ||
            MathF.Abs(packet.Ai1) > 0.001f ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > weapon.ImpossibleSpawnCenterDistancePixels * weapon.ImpossibleSpawnCenterDistancePixels ||
            trustedClientUseCadence.IsOnCooldown(connection.Player, tick))
        {
            return ClientProjectileProvenanceResolveResult.Rejected;
        }

        float canonicalSpeed = speedEnvelope.CanonicalMagnitude;
        float velocityScale = canonicalSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            weapon.ProjectileType,
            connection.Player.Slot.Value,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX * velocityScale,
            packet.VelocityY * velocityScale,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)expectedDamage),
            KnockBack: expectedKnockBack,
            OriginalDamage: 0);

        RuntimePlayerInventoryItem remaining = !weapon.Consumable
            ? weaponItem
            : weaponItem.Stack == 1
                ? default
                : weaponItem with { Stack = checked((short)(weaponItem.Stack - 1)) };
        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            new RuntimePlayerInventoryMutation(checked((short)selectedSlot), remaining),
            ManaCost: 0,
            speedEnvelope,
            weapon.UseTimeTicks) { UseTick = tick };
        return ClientProjectileProvenanceResolveResult.Accepted;
    }


    internal static bool PrepareAmmoConservation(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaPlayerCombatSnapshot attacker,
        VanillaUnifiedRandom1458 random,
        bool ammoBox = false,
        bool ammoPotion = false)
    {
        return VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(
            in weapon, in ammo, in attacker, random.Next, ammoBox, ammoPotion);
    }

    private static int FindFirstAmmo(
        VanillaProjectileAmmoFamily family,
        ReadOnlySpan<RuntimePlayerInventoryItem> inventory,
        int start,
        int endExclusive,
        out RuntimePlayerInventoryItem ammoItem,
        out VanillaProjectileAmmoCombatDefinition ammo)
    {
        ammoItem = default;
        ammo = default;
        for (int slot = start; slot < endExclusive; slot++)
        {
            RuntimePlayerInventoryItem candidate = inventory[slot];
            if (candidate.IsEmpty || !VanillaProjectileWeaponCombatCatalog.IsAmmoType(family, candidate.ItemType))
                continue;

            // Terraria ammo itself is not prefixable here. Unknown compatible ammo is recognized by family but remains
            // fail-closed rather than allowing a later supported stack to leapfrog PickAmmo's first valid candidate.
            if (candidate.Prefix != VanillaPrefixIds.None ||
                !VanillaProjectileWeaponCombatCatalog.TryGetAmmo(family, candidate.ItemType, out ammo))
            {
                return -2;
            }

            ammoItem = candidate;
            return slot;
        }
        return -1;
    }
}
