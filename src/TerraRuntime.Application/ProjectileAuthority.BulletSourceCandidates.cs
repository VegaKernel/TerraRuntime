using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private ClientProjectileProvenanceResolveResult TryResolveBulletSourceCandidates(
        ConnectionHandle connection, in TerrariaProjectileUpdateState packet,
        in RuntimePlayerInventoryItem weaponItem, in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo, in RuntimePlayerInventoryItem ammoItem,
        in VanillaPlayerCombatSnapshot combat, int ammoSlot, bool ammoBox, bool ammoPotion,
        float dx, float dy, float maximumDistance, long tick, VanillaUnifiedRandom1458 plannedRandom,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        // Packet27 has no client platform field. Both complete source profiles start from the
        // same cursor; pending reports may eliminate a profile, never combine its children.
        var before = plannedRandom.Clone();
        bool found = false;
        Span<VanillaBulletLaunchShot1458> shots = stackalloc VanillaBulletLaunchShot1458[VanillaBulletWeaponLaunch1458.MaximumShotCount];
        for (int profile = 0; profile < 2; profile++)
        {
            var arithmetic = profile == 0 ? VanillaBulletSourceArithmetic1458.CoreClrSingle
                : VanillaBulletSourceArithmetic1458.WindowsClr4X86;
            if (!VanillaBulletWeaponStats1458.TryResolve(weapon.Type, weaponItem.Prefix, arithmetic, out var stats) ||
                !VanillaBulletWeaponStats1458.TryGetPrefixModifiers(weapon.Type, weaponItem.Prefix, arithmetic, out var prefix) ||
                !VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out var type))
                continue;
            int damage = VanillaProjectileWeaponCombatCatalog.ResolveDamage(in weapon, in ammo, in prefix, in combat, arithmetic);
            float knockBack = VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(in weapon, in ammo, in prefix, in combat);
            var speed = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(in weapon, in ammo, in prefix, in combat);
            int useTime = Math.Max(1, stats.UseTime);
            if (!speed.IsValid || packet.ProjectileType != type.Value || packet.Damage != damage ||
                packet.OriginalDamage != 0 || MathF.Abs(packet.KnockBack - knockBack) > MathF.Max(0.001f, MathF.Abs(knockBack) * 0.00001f) ||
                packet.Ai0 != 0 || packet.Ai1 != 0 || packet.Ai2 != 0 || packet.BannerIdToRespondTo != 0 ||
                dx * dx + dy * dy > maximumDistance * maximumDistance ||
                trustedClientUseCadence.IsOnCooldown(connection.Player, tick))
                continue;
            var cursor = before.Clone();
            bool conserved = PrepareAmmoConservation(in weapon, in ammo, in combat, cursor, ammoBox, ammoPotion);
            if (!VanillaBulletWeaponLaunch1458.TryResolveFromFirstVelocity(weapon.Type, packet.VelocityX, packet.VelocityY,
                speed.CanonicalMagnitude, type, cursor.Next, cursor.NextDouble, shots, out int count, arithmetic))
                continue;
            var states = new ProjectileStateUpdate[count];
            for (int index = 0; index < count; index++)
                states[index] = new(type, connection.Player.Slot.Value, packet.PositionX, packet.PositionY,
                    shots[index].VelocityX, shots[index].VelocityY, default, BannerIdToRespondTo: 0,
                    Damage: checked((short)damage), KnockBack: knockBack, OriginalDamage: 0);
            if (!MatchesBulletReport(in packet, in states[0])) continue;
            RuntimePlayerInventoryItem remaining = conserved ? ammoItem : ammoItem.Stack == 1
                ? default : ammoItem with { Stack = checked((short)(ammoItem.Stack - 1)) };
            var candidate = new AuthoritativeClientProjectileSpawn(states[0],
                new RuntimePlayerInventoryMutation(checked((short)ammoSlot), remaining), 0, speed, useTime)
                { UseTick = tick, VolleyStates = states, RandomAfter = cursor };
            if (!found)
            {
                authoritative = candidate;
                plannedRandom.CopyStateFrom(cursor);
                found = true;
            }
            else if (states.Length == authoritative.VolleyStates!.Length && (authoritative.UseTimeTicks != candidate.UseTimeTicks ||
                !authoritative.VolleyStates!.AsSpan().SequenceEqual(states) ||
                authoritative.InventoryMutation != candidate.InventoryMutation ||
                !authoritative.RandomAfter!.HasSameState(cursor)))
                authoritative = authoritative with { AlternativeBulletUse = new(candidate) };
        }
        return found ? ClientProjectileProvenanceResolveResult.Accepted : ClientProjectileProvenanceResolveResult.Rejected;
    }
}
