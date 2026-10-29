using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

// An invocation context on the existing shared stream, not a second random owner or buff clock.
internal readonly record struct ProjectilePlayerHurt1458(
    ProjectileTypeId Type, int MaximumLife, bool GoodWorld, VanillaUnifiedRandom1458 Random)
{
    internal bool TryResolveStatusAfterSurvivingHurt(int damage, bool expert, bool master,
        out BuffTypeId type, out int duration)
    {
        int count = VanillaGroundCombatPlayerHit1458.SurvivingBloodSamples(damage, MaximumLife);
        for (int i = 0; i < count; i++) Random.NextDouble();
        int sourceDuration;
        switch (Type.Value)
        {
            case 436:
                if (Random.Next(5) < 2) { type = default; duration = 0; return false; }
                type = new(31); sourceDuration = 300; break;
            case 435:
                if (Random.Next(3) == 0) { type = default; duration = 0; return false; }
                type = new(144); sourceDuration = 300; break;
            case 437:
                type = new(144); sourceDuration = 60 * Random.Next(4, 9); break;
            default: type = default; duration = 0; return false;
        }
        duration = VanillaNpcContactBuffs1458.ResolveDuration(type, sourceDuration,
            expert || GoodWorld, master || expert && GoodWorld, resistCold: false);
        return true;
    }

}

internal sealed partial class PlayerAuthority
{
    internal bool CanOwnGroundCombatHurt(in IncomingPlayerCombatCapture1458 capture)
    {
        if (capture.GodMode) return true; // Hurt returns before voice and life-maximum use.
        if (capture.Player.DerivedLifeMax is not (>= 1 and <= short.MaxValue) ||
            (capture.Vitals?.Health.Life ?? capture.Player.Life) > capture.Player.DerivedLifeMax ||
            !TryCaptureEquipment(capture.Player.Player, out var equipment)) return false;
        // Voice override is not packet-4 voice variant. Unknown vanity voice work remains unowned.
        foreach (var item in equipment)
            if (item.Stack > 0 && VanillaPlayerItemSlotCatalog.IsVanityArmorSlot(item.SlotId)) return false;
        return true;
    }

    private NpcContactBuff1458[] ApplyProjectileStatus(RuntimePlayerMember target,
        in VanillaPlayerCombatSnapshot combat, in ProjectilePlayerHurt1458 source, int damage)
    {
        if (!source.TryResolveStatusAfterSurvivingHurt(damage, expertMode, masterMode, out var type, out int duration)) return [];
        if (transferProfiles.CaptureBuffState(target.Connection) is not { } before) return [];
        var next = before.Clone();
        if (!next.TryApplyNpcContactBuff(type, duration, combat.IsImmuneToNpcContactBuff(type)) ||
            !transferProfiles.TryAdoptBuffState(target.Connection, before, next)) return [];
        return [new(type, next.GetDuration(type))];
    }
}

internal sealed partial class ServerPlayerAuthority
{
    internal bool CanOwnGroundCombatHurt(in PlayerStateSnapshot current)
    {
        if (current.GodMode) return true;
        if (current.DerivedLifeMax is not (>= 1 and <= short.MaxValue) || current.Life > current.DerivedLifeMax) return false;
        for (short slot = VanillaPlayerItemSlotCatalog.VanityArmorStart;
             slot < VanillaPlayerItemSlotCatalog.VanityArmorEndExclusive; slot++)
            if (!states.TryGetItem(current.Player, slot, out var item) || !item.IsEmpty) return false;
        return true;
    }

    private bool ApplyProjectileStatus(PlayerHandle target, in VanillaPlayerCombatSnapshot combat,
        in ProjectilePlayerHurt1458 source, int damage, bool expert, bool master) =>
        source.TryResolveStatusAfterSurvivingHurt(damage, expert, master, out var type, out int duration) &&
        TryApplyNpcContactBuff(target, type, duration, combat.IsImmuneToNpcContactBuff(type), publish: false);
}
