using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly RuntimePlayerProjectileUseCapture?[] plannedMoonPlayerBodies = new RuntimePlayerProjectileUseCapture?[255];

    private bool AreMoonPlayerBodiesCurrent()
    {
        foreach (var capture in plannedMoonPlayerBodies)
            if (capture is not null && !playerAuthority.IsCurrentProjectileUse(capture)) return false;
        return true;
    }

    private bool TryExecuteMoonBossLoot(in NpcSnapshot dead)
    {
        if (!IsPreviewingDeath || !VanillaNpcDefinitionCatalog.TryGet(dead.TypeIdentity, dead.NetIdentity, out var definition) ||
            !interactions.TryCopyInteractingSlots(dead.Handle, interactionSlots, out int count)) return false;
        Span<bool> interacting = stackalloc bool[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
        interacting.Clear();
        for (int index = 0; index < count; index++) interacting[interactionSlots[index].Value] = true;
        if (TryGetPreparedProjectileInteraction(in dead, out var projected)) interacting[projected.Slot.Value] = true;
        Span<VanillaMoonBossLootPlayer1458> recipients = stackalloc VanillaMoonBossLootPlayer1458[interacting.Length];
        int recipientCount = 0;
        for (int slot = 0; slot < interacting.Length; slot++)
        {
            if (!interacting[slot] || !plannedPlayerPresence[slot]) continue;
            var player = plannedPlayers[slot];
            if (masterMode && VanillaMoonBossLootEvaluator1458.IsBoss(dead.TypeIdentity) &&
                (dead.Type < 338 ? DeathClock?.PumpkinMoonActive == true : DeathClock?.SnowMoonActive == true))
            {
                // Portable Stool/Hand of Creation can resize Player.Center without a
                // mount. Reuse the paid-boss body's exact profile owner; its unmodeled
                // stool and mounted lifetimes are refused, never replaced by 20x42.
                if (player.HasMount || !playerAuthority.TryCaptureProjectileUse(player.Player, out var body) ||
                    body is not { HasProfile: true } || body.Player != player ||
                    body.Equipment.Any(equipment => equipment.ItemNetId is 4341 or 5126)) return false;
                plannedMoonPlayerBodies[slot] = body;
            }
            var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (VanillaPlayerWidth, VanillaPlayerHeight);
            recipients[recipientCount++] = new(new PlayerSlotId((byte)slot),
                player.PositionX + size.Item1 * .5f, player.PositionY + size.Item2 * .5f);
        }
        bool? christmas = DeathClock is { HasOwnedSeasonalState: true } seasonalClock
            ? seasonalClock.ChristmasActive : plannedGlobalLootWorld?.Christmas;
        var context = new VanillaMoonBossLootContext1458(dead.TypeIdentity,
            DeathClock?.PumpkinMoonActive, DeathClock?.SnowMoonActive, DeathClock?.MoonEventWaveNumber, expertMode, masterMode,
            christmas);
        var origin = ResolveNpcLootOrigin(in dead, in definition);
        if (!VanillaMoonBossLootEvaluator1458.TryExecute(in context, in origin, recipients[..recipientCount],
                random, lootDelivery, out var result)) return false;
        plannedMoonMoneyCleared = result.MoneyCleared;
        return true;
    }
}
