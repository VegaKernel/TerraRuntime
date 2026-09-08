using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    // World-writer state for clientless players only. Client packet50 is never a duration input.
    private struct LavaState
    {
        public PlayerHandle Owner;
        public int Protection;
        public int BurningTicks;
        public int RegenLoss;
        public bool PublishedBurning;
    }

    internal void TickLava(long tick, WorldTileStore tiles, bool expertMode, bool masterMode, bool remixWorld)
    {
        int count = states.CopySnapshots(snapshots);
        for (int i = 0; i < count; i++)
        {
            PlayerStateSnapshot player = snapshots[i];
            if (!player.HasHealth || player.IsDead || player.Life <= 0)
            {
                ResetLavaState(player.Player);
                continue;
            }
            if (!TryCaptureCombatSnapshot(player.Player, out VanillaPlayerCombatSnapshot equipment)) continue;
            ref LavaState lava = ref lavaStates[player.Player.Slot.Value];
            if (lava.Owner != player.Player)
                lava = new LavaState { Owner = player.Player, Protection = equipment.LavaProtectionTicks };

            // Player.UpdateBuffs -> UpdateLifeRegen precedes this update's liquid collision.
            // Ordinary OnFire blocks positive regeneration and subtracts eight / 120 HP per update.
            if (lava.BurningTicks > 0)
            {
                lava.BurningTicks--;
                lava.RegenLoss += 8;
                if (lava.RegenLoss >= 120)
                {
                    lava.RegenLoss -= 120;
                    if (!player.GodMode)
                    {
                        var burn = new FinalDamageToHp(1, new TargetMitigation(0, 0, 0, false, false, true));
                        if (CommitResolvedDamage(tick, in player,
                                DamageSource.FromEnvironment(EnvironmentDamageCause.Burning), default, in burn,
                                0, default, false, damageOverTime: true, out var burned) == PlayerDamageCommitResult.Committed)
                        {
                            player = burned;
                            if (player.IsDead) continue;
                        }
                    }
                }
            }

            int height = (int)PlayerAuthority.VanillaBasePlayerHeight - (equipment.WaterWalk ? 6 : 0);
            bool touching = VanillaWorldCollision.LavaCollision(tiles, player.PositionX, player.PositionY,
                (int)PlayerAuthority.VanillaBasePlayerWidth, height);
            if (touching)
            {
                if (!damageImmunity.IsPveImmune(player.Player, VanillaPlayerImmunityChannel1458.Lava, tick))
                {
                    if (lava.Protection > 0) lava.Protection--;
                    else
                    {
                        int damage = (remixWorld ? 200 : 80) - (equipment.LavaRose ? 45 : 0);
                        int duration = (remixWorld ? 630 : 420) - (equipment.LavaRose ? 210 : 0);
                        if (TryCommitAuthoritativeDamage(tick, player.Player,
                                DamageSource.FromEnvironment(EnvironmentDamageCause.Lava), default, damage, 0,
                                VanillaPlayerImmunityChannel1458.Lava, expertMode, masterMode, out var struck) == PlayerDamageCommitResult.Committed)
                        {
                            if (struck.IsDead) continue;
                            lava.BurningTicks = Math.Max(lava.BurningTicks, duration);
                        }
                    }
                }
            }
            else
            {
                if (lava.Protection < equipment.LavaProtectionTicks) lava.Protection++;
                var wet = VanillaWorldCollision.GetLiquidContacts(tiles, player.PositionX, player.PositionY,
                    (int)PlayerAuthority.VanillaBasePlayerWidth, height);
                if (wet.Wet) lava.BurningTicks = 0;
            }
            lava.Protection = Math.Min(lava.Protection, equipment.LavaProtectionTicks);
            PublishBurning(ref lava);
        }
    }

    private void PublishBurning(ref LavaState lava)
    {
        bool burning = lava.BurningTicks > 0;
        if (burning == lava.PublishedBurning) return;
        lava.PublishedBurning = burning;
        Span<BuffTypeId> buffs = stackalloc BuffTypeId[1];
        if (burning) buffs[0] = new BuffTypeId(24);
        events?.ServerPlayerBuffTypesUpdated(lava.Owner, buffs[..(burning ? 1 : 0)]);
    }

    private void ResetLavaState(PlayerHandle player)
    {
        ref LavaState lava = ref lavaStates[player.Slot.Value];
        if (lava.Owner != player) return;
        lava.BurningTicks = 0;
        PublishBurning(ref lava);
        lava = default;
    }
}
