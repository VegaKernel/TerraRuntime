using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private bool TryApplyServerStrike(in NpcDamageRequest request, out NpcDamageResult result, NpcSnapshot? sharedLifeOwner = null)
    {
        if (!npcs.TryGet(request.Target, out var before))
        {
            result = default;
            return false;
        }
        if (!TryPlanTownStrike(in before, request.HitDirection, out var townStrike))
        { result = default; return false; }
        bool applied, spawnTrueEye, forceUpdate;
        NpcSnapshot committed;
        pendingTownStrike = townStrike;
        try
        {
            applied = damage.TryApplyUnpublished(in request, out result, out committed, out spawnTrueEye,
                out forceUpdate, sharedLifeOwner, townStrike?.Reaction);
        }
        finally { pendingTownStrike = null; }
        if (!applied)
            return false;

        // Guide-doll admission claims its source whole-stack removal together with all death drops.
        // Publish that already accepted removal before StrikeNPC's packet28; no arbitrary callback
        // participates in admission or can consume the doll on a rejected unknown death dependency.
        if (guideDollRemoval.HasValue && (pendingDeathPlan is null || !pendingDeathPlan.TryPublishPrecedingRemoval()))
            throw new InvalidOperationException("Accepted Guide doll death lost its source removal claim.");

        // TerrariaServer 1.4.5.8 StrikeNPC_Inner sends the supplied damage/critical flag, rather than HP damage.
        // SendData takes number2 as float and narrows it to short. The supported net11 Windows execution
        // saturates large positive values; an unchecked integer short cast produces different wire bytes.
        var wire = new TerrariaNpcDamageState(committed.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(committed.Handle.Generation),
            (short)Math.Min(request.BaseDamage, short.MaxValue), request.KnockBack,
            checked((byte)(request.HitDirection + 1)), request.Critical ? (byte)1 : (byte)0);
        npcs.TryPublishPendingBirthBeforeStrike(in before, in committed);
        npcReplication?.TryPublishDamage(default, in wire);
        PublishTownStrikeRandom(townStrike);
        if (!result.Lethal) ExecuteNpcNonlethalHitEffects(in committed);

        // A lethal actor is published by the final despawn, after owned loot/death effects. Publishing the
        // intermediate Life=0 state here would insert another packet 23 ahead of those effects.
        if (!damage.TryCompleteUnpublished(in committed, spawnTrueEye, publishUpdate: !result.Lethal, forceUpdate))
            throw new InvalidOperationException("An accepted server strike lost its exact NPC revision before publication.");
        return true;
    }

    private void PublishNpcDamage(NpcHandle handle, GameCommandSourceId excludedSource,
        in TerrariaNpcDamageState wire)
    {
        // Source SendData(28) flushes spawnNeedsSyncing before the strike frame. Use the exact
        // handle, never just a slot or wrapped wire generation which could refer to a replacement.
        npcs.TryPublishPendingBirth(handle);
        npcReplication?.TryPublishDamage(excludedSource, in wire);
    }
}
