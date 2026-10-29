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
            if (!states.TryGet(player.Player, out var livePlayer) || livePlayer != player) continue;
            if (!player.HasHealth || player.IsDead || player.Life <= 0)
            {
                ResetLavaState(player.Player);
                continue;
            }
            if (!TryCaptureCombatSnapshot(player.Player, out VanillaPlayerCombatSnapshot equipment)) continue;
            ref LavaState lava = ref lavaStates[player.Player.Slot.Value];
            if (lava.Owner != player.Player)
                lava = new LavaState { Owner = player.Player, Protection = equipment.LavaProtectionTicks };

            // Player.UpdateBuffs -> UpdateLifeRegen precedes this update's liquid collision. The neutral
            // source rates add: poison4, ordinary fire8, cursed fire24 per120 HP. Vampire/dripping-slime,
            // honey and other unrepresented regeneration modifiers are outside this admitted actor slice.
            bool burning = lava.BurningTicks > 0;
            if (burning) lava.BurningTicks--;
            PlayerDebuffSnapshot1458 contactEffects = npcContactBuffOwners[player.Player.Slot.Value] == player.Player
                ? npcContactDebuffEffects[player.Player.Slot.Value] : default;
            int regenLoss = (burning ? 8 : 0) + (contactEffects.Poisoned ? 4 : 0) + (contactEffects.OnFire2 ? 24 : 0) +
                (contactEffects.Electrified ? ((player.ControlFlags & 12) != 0 ? 40 : 8) : 0);
            if (regenLoss > 0)
            {
                lava.RegenLoss += regenLoss;
                if (lava.RegenLoss >= 120)
                {
                    lava.RegenLoss -= 120;
                    if (!player.GodMode)
                    {
                        var burn = new FinalDamageToHp(1, new TargetMitigation(0, 0, 0, false, false, true));
                        if (CommitResolvedDamage(tick, in player,
                                DamageSource.FromEnvironment(contactEffects.Poisoned ? EnvironmentDamageCause.Poison :
                                    contactEffects.Electrified ? EnvironmentDamageCause.Electrified : EnvironmentDamageCause.Burning), default, in burn,
                                0, default, false, damageOverTime: true, out var burned) == PlayerDamageCommitResult.Committed)
                        {
                            player = burned;
                            if (player.IsDead || !states.TryGet(player.Player, out var liveBurned) || liveBurned != player)
                                continue;
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
                            if (struck.IsDead || !states.TryGet(player.Player, out var liveStruck) || liveStruck != struck)
                                continue;
                            player = struck;
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

    private void PublishBurning(ref LavaState lava, bool publish = true)
    {
        bool burning = lava.BurningTicks > 0;
        if (burning == lava.PublishedBurning) return;
        if (burning) moonLeechBeforeBurning[lava.Owner.Slot.Value] = GetMoonLeechDuration(lava.Owner) > 0;
        lava.PublishedBurning = burning;
        if (publish) PublishBuffTypes(lava.Owner);
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
