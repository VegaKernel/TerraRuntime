using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

using static TerraRuntime.Application.Bots.BotPolicy;

namespace TerraRuntime.Application.Bots;

internal sealed partial class RuntimeBotCombat
{
    private bool TryGuardPreparedMeleeAttack(in PlayerStateSnapshot expected, in BotGuardTarget target,
        long tick, out bool owned)
    {
        owned = false;
        if (!target.Npc.IsAssigned ||
            !VanillaItemCombatCatalog.TryGetDirectMelee(bot.Loadout.MeleeWeapon, out var weapon)) return false;
        byte slot = ResolveWeaponSlot(bot.Configuration.WeaponPolicy, RuntimeBotAttackKind.Melee);
        if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out var held) || held.IsEmpty ||
            held.ItemType != weapon.Type || held.Prefix != VanillaPrefixIds.None ||
            !inventory.TryBuildBotCombatSnapshot(bot, tick, out var combat) ||
            !npcs.TryCapture(target.Npc, out var initial) ||
            !VanillaNpcDefinitionCatalog.TryGet(initial.TypeIdentity, initial.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(initial.Simulation, out var body)) return false;
        float dx = initial.PositionX + body.Width * .5f - (expected.PositionX + PlayerAuthority.VanillaBasePlayerWidth * .5f);
        float dy = initial.PositionY + body.Height * .5f - (expected.PositionY + PlayerAuthority.VanillaBasePlayerHeight * .5f);
        int direction = dx < 0f ? -1 : 1;
        var maximum = VanillaDirectMeleeCombatMath.Resolve(weapon, VanillaCombatPrefixModifiers.Identity,
            combat, 15, 1, pvp: false);
        if (!npcs.TryCaptureOrdinaryNonlethalMelee(target.Npc, expected.Player, maximum.MaximumDamage,
                maximum.ArmorPenetration, maximum.KnockBack, direction, out var captured)) return false;
        // From this owned attempt onward, stale admission never falls back or takes new random offers.
        owned = true;
        if (dx * dx + dy * dy > ConservativeMeleeCenterDistancePixels * ConservativeMeleeCenterDistancePixels)
            return false;
        var command = CaptureAttackCommand();
        int useTime = Math.Max(1, maximum.UseTimeTicks), animation = Math.Max(1, maximum.AnimationTicks);
        if (tick != bot.CurrentTick || tick < 0 || tick > long.MaxValue - useTime ||
            tick > long.MaxValue - animation || !projectiles.CanUseTrustedItem(expected, tick, useTime)) return false;
        var presentation = new ServerPlayerStateStore.ItemUsePresentation(slot, true, direction, 0f, animation);
        if (!serverPlayers.TryPrepareItemUse(bot.ServerPlayerId, expected, presentation, out var item) || item is null)
            return false;
        var source = expected;
        bool Current() => projectiles.CanUseTrustedItem(source, tick, useTime) &&
            item.IsCurrent && IsCurrentAttackCommand(command) &&
            npcs.TryCapture(captured.Handle, out var live) && live == captured;
        if (!Current()) return false;
        // Preserve the existing BOT Random.Shared component order; no Main/source cursor is claimed.
        var resolved = VanillaDirectMeleeCombatMath.Resolve(weapon, VanillaCombatPrefixModifiers.Identity,
            combat, Random.Shared.Next(-15, 16), Random.Shared.Next(1, 101), pvp: false);
        var request = new NpcDamageRequest(captured.Handle, DamageSource.FromPlayerItem(source.Player),
            resolved.Damage, resolved.ArmorPenetration, resolved.Critical, resolved.KnockBack, direction);
        void AdoptAndPublish(NpcSnapshot accepted)
        {
            if (!item.TryAdoptUnpublished())
                throw new InvalidOperationException("Validated melee player changed during callback-free adoption.");
            projectiles.MarkAcceptedTrustedItemUse(source.Player, tick, useTime);
            bot.NextAttackTick = tick + useTime;
            bot.UseItemUntilTick = tick + animation;
            item.TryPublishItemUse();
        }
        return npcs.TryStrikePreparedBotMelee(captured, request, Current, AdoptAndPublish);
    }
}
