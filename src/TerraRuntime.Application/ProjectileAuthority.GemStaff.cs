using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;
internal sealed partial class ProjectileAuthority
{
    private ClientProjectileProvenanceResolveResult TryResolveStrictGemStaffSpawn(ConnectionHandle connection,
        in PlayerStateSnapshot player,
        in RuntimePlayerInventoryItem selected,
        in TerrariaProjectileUpdateState packet,
        VanillaUnifiedRandom1458 random,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        if (!players.HasRemotePlayerPhaseBinding ||
            players.MagicItemWindowsArithmetic is not { } windows ||
            !VanillaGemStaff1458.TryResolveWeapon(selected.ItemType,
            selected.Prefix,
            windows,
            out var weapon,
            out _) ||
            !players.TryPrepareGemStaffPayment(connection,
            selected,
            out var payment,
            out int debit,
            out var combat,
            out var magicUse) ||
            !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(weapon.Projectile,
            out var definition) ||
            packet.ProjectileType != weapon.Projectile.Value ||
            packet.OriginalDamage != 0 ||
            packet.BannerIdToRespondTo != 0 ||
            BitConverter.SingleToInt32Bits(packet.Ai2) != 0 ||
            BitConverter.SingleToInt32Bits(packet.Ai0) != (int)payment.GemFeatures ||
            packet.Ai1 != 0f)
            return ClientProjectileProvenanceResolveResult.Rejected;
        float speedFactor = 1f;
        if ((payment.GemFeatures & GemStaffFeatures1458.FastThenSlow) != 0)
            speedFactor *= 5f;
        if ((payment.GemFeatures & GemStaffFeatures1458.CanBounce) != 0)
            speedFactor *= 1.25f;
        float speed = weapon.ShootSpeed * speedFactor;
        float length = MathF.Sqrt(packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY);
        if (!(length > 0f) || !float.IsFinite(length) || MathF.Abs(length - speed) > 0.002f)
            return ClientProjectileProvenanceResolveResult.Rejected;
        int damage = Math.Max(1, (int)(weapon.BaseDamage * combat.MagicDamage + 0.000005f));
        Span<GemStaffLaunch1458> launch = stackalloc GemStaffLaunch1458[VanillaGemStaff1458.MaximumVolleySize];
        if (!VanillaGemStaff1458.TryPlanVolley(selected.ItemType,
            payment.EffectiveBody,
            player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
            player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
            packet.VelocityX / speedFactor,
            packet.VelocityY / speedFactor,
            damage,
            weapon.KnockBack,
            random.NextDouble,
            launch,
            out int count))
            return ClientProjectileProvenanceResolveResult.Rejected;
        var states = new ProjectileStateUpdate[count];
        for (int i = 0; i < count; i++)
        {
            var child = launch[i];
            states[i] = new(child.Projectile,
                connection.Player.Slot.Value,
                child.CenterX - definition.Width * 0.5f,
                child.CenterY - definition.Height * 0.5f,
                child.VelocityX,
                child.VelocityY,
                new(child.Ai0,
                child.Ai1,
                0f),
                0,
                checked((short)child.Damage),
                child.KnockBack,
                0);
        }

        if (!MatchesBulletReport(in packet, in states[0]))
            return ClientProjectileProvenanceResolveResult.Rejected;
        // The payment owns the exact local start/midpoint entitlement. Generic cadence
        // cannot create or renew that entitlement, including completed packet replays.
        authoritative = new(states[0], null, debit, new(speed, speed), payment.SlowMagicUse ? Math.Max(1, (int)(weapon.UseTime * 2.5f)) : weapon.UseTime)
        {
            UseTick = tickProvider(),
            MagicPayment = payment,
            MagicUse = magicUse,
            VolleyStates = states,
            GemStaffFeatures = payment.GemFeatures
        };
        return ClientProjectileProvenanceResolveResult.Accepted;
    }
}
