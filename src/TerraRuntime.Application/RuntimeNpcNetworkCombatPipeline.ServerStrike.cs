using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private bool TryApplyServerStrike(in NpcDamageRequest request, out NpcDamageResult result)
    {
        if (!damage.TryApplyUnpublished(in request, out result, out var committed, out bool spawnTrueEye, out bool forceUpdate))
            return false;

        // TerrariaServer 1.4.5.8 StrikeNPC_Inner sends the supplied damage/critical flag, rather than HP damage.
        // SendData takes number2 as float and narrows it to short. The supported net11 Windows execution
        // saturates large positive values; an unchecked integer short cast produces different wire bytes.
        var wire = new TerrariaNpcDamageState(committed.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(committed.Handle.Generation),
            (short)Math.Min(request.BaseDamage, short.MaxValue), request.KnockBack,
            checked((byte)(request.HitDirection + 1)), request.Critical ? (byte)1 : (byte)0);
        npcReplication?.TryPublishDamage(default, in wire);

        // A lethal actor is published by the final despawn, after owned loot/death effects. Publishing the
        // intermediate Life=0 state here would insert another packet 23 ahead of those effects.
        if (!damage.TryCompleteUnpublished(in committed, spawnTrueEye, publishUpdate: !result.Lethal, forceUpdate))
            throw new InvalidOperationException("An accepted server strike lost its exact NPC revision before publication.");
        return true;
    }
}
