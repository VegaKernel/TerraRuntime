using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly bool? onlyShimmerOceanWorlds;

    private bool TryCaptureDeathPreludeContext(in NpcSnapshot dead, bool eaterBoss,
        out RuntimeNpcDeathPreludeContext1458 context)
    {
        context = default;
        if (!interactions.TryCopyInteractingSlots(dead.Handle, interactionSlots, out int count)) return false;
        bool hasProjectedInteraction = TryGetPreparedProjectileInteraction(in dead, out var projectedPlayer);
        var recipients = new PlayerHandle[count + (hasProjectedInteraction ? 1 : 0)];
        int recipientCount = 0;
        for (int index = 0; index < count; index++)
            if (players.TryGetPlayer(interactionSlots[index], out var player))
                recipients[recipientCount++] = player.Player;

        if (hasProjectedInteraction && !recipients.AsSpan(0, recipientCount).Contains(projectedPlayer))
        {
            int insertion = recipientCount;
            while (insertion > 0 && recipients[insertion - 1].Slot.Value > projectedPlayer.Slot.Value)
            {
                recipients[insertion] = recipients[insertion - 1];
                insertion--;
            }
            recipients[insertion] = projectedPlayer;
            recipientCount++;
        }

        bool twinPeerAlive = false;
        if (VanillaMechanicalBossLootEvaluator.IsTwin(dead.TypeIdentity))
        {
            var other = dead.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            for (int index = 0; index < DeathNpcs.Capacity; index++)
                if (DeathNpcs.TryGetActive((byte)index, out var peer) && peer.TypeIdentity == other)
                { twinPeerAlive = true; break; }
        }
        bool hasBannerPlayer = TryFindBannerPlayer(in dead, out var bannerPlayer, out string bannerName);
        bool hasOwnInteractions = hasProjectedInteraction || interactions.HasAnyInteraction(dead.Handle);
        // Admitted shared-life death ingress resolves the root before this point. Family participation
        // is propagated by its existing interaction owner; unrelated raw realLife links are not invented.
        // Custom hosts without the retained seed projection reject relevant dungeon gates rather
        // than assume the special-world flag. Standard composition supplies the exact source getter.
        context = new(hasOwnInteractions, eaterBoss, twinPeerAlive,
            plannedGlobalLootWorld?.HardMode == true || DeathProgression.IsCompleted(VanillaWorldProgressionId.Hardmode),
            DeathClock?.GetGoodWorld == true || npcSpecificGoodWorld,
            isThereAWorldSurface, plannedGlobalLootWorld?.SkeletronDowned == true ||
                DeathProgression.IsCompleted(VanillaWorldProgressionId.Skeletron),
            onlyShimmerOceanWorlds, recipients.AsMemory(0, recipientCount),
            hasBannerPlayer ? bannerPlayer.Player.Slot.Value : -1,
            hasBannerPlayer ? bannerName : null, hasOwnInteractions);
        return true;
    }

    private bool TryGetPreparedProjectileInteraction(in NpcSnapshot npc, out PlayerHandle player)
    {
        player = default;
        if (!IsPreviewingDeath || pendingProjectileStrike is not { } strike || strike.Target != npc.Handle ||
            !players.TryGetPlayer(strike.Player.Slot, out var current) || current.Player != strike.Player)
            return false;
        player = strike.Player;
        return true;
    }
}
