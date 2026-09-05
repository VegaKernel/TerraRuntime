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

        if (!VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(weaponItem.Prefix, out VanillaCombatPrefixModifiers prefix) ||
            !players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat))
        {
            // An unsupported prefix/equipment combination may still be a legitimate vanilla shot, but it cannot cross
            // the CombatTrusted boundary until its exact source formula is imported.
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

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

        if (!VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out ProjectileTypeId expectedProjectileType))
            return ClientProjectileProvenanceResolveResult.NotApplicable;

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

        if (packet.ProjectileType != expectedProjectileType.Value ||
            packet.Damage != expectedDamage ||
            packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            !speedEnvelope.ContainsMagnitude(packetSpeed) ||
            MathF.Abs(packet.Ai0) > 0.001f ||
            MathF.Abs(packet.Ai1) > 0.001f ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > maximumDistance * maximumDistance ||
            trustedClientUseCadence.IsOnCooldown(connection.Player, tick, authoritativeUseTime))
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
            packet.VelocityX * velocityScale,
            packet.VelocityY * velocityScale,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)expectedDamage),
            KnockBack: expectedKnockBack,
            OriginalDamage: 0);

        int weaponConservationRoll = weapon.WeaponAmmoConservationOneIn > 0
            ? Random.Shared.Next(weapon.WeaponAmmoConservationOneIn)
            : -1;
        int quiverConservationRoll = weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow && attackerCombat.MagicQuiver
            ? Random.Shared.Next(5)
            : -1;
        bool conserveAmmo = VanillaProjectileWeaponCombatCatalog.ShouldConserveAmmo(
            in weapon, in ammo, in attackerCombat, weaponConservationRoll, quiverConservationRoll);
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
            authoritativeUseTime);
        return ClientProjectileProvenanceResolveResult.Accepted;

        static ClientProjectileProvenanceResolveResult RejectProvenance() =>
            ClientProjectileProvenanceResolveResult.Rejected;
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
            trustedClientUseCadence.IsOnCooldown(connection.Player, tick, weapon.UseTimeTicks))
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
            weapon.UseTimeTicks);
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
            trustedClientUseCadence.IsOnCooldown(connection.Player, tick, weapon.UseTimeTicks))
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
            weapon.UseTimeTicks);
        return ClientProjectileProvenanceResolveResult.Accepted;
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
