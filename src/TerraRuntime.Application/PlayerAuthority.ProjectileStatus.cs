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
    internal bool HasStatusAfterSurvivingHurt(int damage)
    {
        int count = VanillaGroundCombatPlayerHit1458.SurvivingBloodSamples(damage, MaximumLife);
        for (int i = 0; i < count; i++) Random.NextDouble();
        return Type.Value == 436 && Random.Next(5) >= 2;
    }

    internal int ConfusedDuration(bool expert, bool master) =>
        VanillaNpcContactBuffs1458.ResolveDuration(new BuffTypeId(31), 300,
            expert || GoodWorld, master || expert && GoodWorld, resistCold: false);
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
        if (!source.HasStatusAfterSurvivingHurt(damage)) return [];
        if (transferProfiles.CaptureBuffState(target.Connection) is not { } before) return [];
        var type = new BuffTypeId(31);
        int duration = source.ConfusedDuration(expertMode, masterMode);
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
        source.HasStatusAfterSurvivingHurt(damage) &&
        TryApplyNpcContactBuff(target, new BuffTypeId(31), source.ConfusedDuration(expert, master),
            combat.IsImmuneToNpcContactBuff(new BuffTypeId(31)), publish: false);
}
