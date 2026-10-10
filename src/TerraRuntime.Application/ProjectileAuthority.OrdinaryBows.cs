using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private ClientProjectileProvenanceResolveResult TryResolveOrdinaryBowSourceCandidates(
        ConnectionHandle connection, in TerrariaProjectileUpdateState packet,
        in RuntimePlayerInventoryItem weaponItem, in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo, in RuntimePlayerInventoryItem ammoItem,
        in VanillaPlayerCombatSnapshot combat, int ammoSlot, bool ammoBox, bool ammoPotion,
        float playerCenterX, float playerCenterY, long tick, VanillaUnifiedRandom1458 plannedRandom,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        if (packet.OriginalDamage != 0 || packet.BannerIdToRespondTo != 0 ||
            packet.Ai0 != 0f || packet.Ai1 != 0f || packet.Ai2 != 0f ||
            !VanillaOrdinaryBowLaunch1458.IsValidSpawnCenter(packet.PositionX + 5f, packet.PositionY + 5f,
                playerCenterX, playerCenterY) || trustedClientUseCadence.IsOnCooldown(connection.Player, tick))
            return ClientProjectileProvenanceResolveResult.Rejected;

        VanillaOrdinaryBowLaunchFacts1458? accepted = null;
        // Packet27 has no source arithmetic identity. Match a complete source profile,
        // including damage/KB/speed; identical bodies retain the shorter proven period.
        for (int profile = 0; profile < 2; profile++)
        {
            var arithmetic = profile == 0 ? VanillaBulletSourceArithmetic1458.CoreClrSingle
                : VanillaBulletSourceArithmetic1458.WindowsClr4X86;
            if (!VanillaOrdinaryBowLaunch1458.TryResolve(weapon.Type, weaponItem.Prefix, ammoItem.ItemType,
                    in combat, out var launch, arithmetic) || packet.ProjectileType != launch.ProjectileType.Value ||
                packet.Damage != launch.Damage ||
                MathF.Abs(packet.KnockBack - launch.KnockBack) > MathF.Max(0.001f, MathF.Abs(launch.KnockBack) * 0.00001f) ||
                !VanillaOrdinaryBowLaunch1458.IsValidVelocity(packet.VelocityX, packet.VelocityY, launch.Speed))
                continue;
            if (accepted is null || launch.UseTime < accepted.Value.UseTime)
                accepted = launch;
        }
        if (accepted is not { } source)
            return ClientProjectileProvenanceResolveResult.Rejected;

        bool conserved = PrepareAmmoConservation(in weapon, in ammo, in combat, plannedRandom, ammoBox, ammoPotion);
        RuntimePlayerInventoryItem remaining = conserved ? ammoItem : ammoItem.Stack == 1
            ? default : ammoItem with { Stack = checked((short)(ammoItem.Stack - 1)) };
        var state = new ProjectileStateUpdate(source.ProjectileType, connection.Player.Slot.Value,
            packet.PositionX, packet.PositionY, packet.VelocityX, packet.VelocityY, default,
            BannerIdToRespondTo: 0, Damage: checked((short)source.Damage), KnockBack: source.KnockBack,
            OriginalDamage: 0);
        authoritative = new(state, new RuntimePlayerInventoryMutation(checked((short)ammoSlot), remaining),
            0, new(source.Speed, source.Speed), source.UseTime)
            { UseTick = tick, RequiresPlainBowPose = true };
        return ClientProjectileProvenanceResolveResult.Accepted;
    }
}
