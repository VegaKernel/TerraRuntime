using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// One NPC spawn requested by a speculative AI transition. Coordinates use vanilla NewNPC semantics:
/// integer X/Y identify the spawned NPC bottom-center, while velocity is applied after SetDefaults.
/// The intent must never mutate the NPC store until the source NPC state commit succeeds.
/// </summary>
public readonly record struct NpcAiSpawnIntent(
    NpcTypeId Type,
    int BottomX,
    int BottomY,
    float VelocityX,
    float VelocityY,
    ushort Target);

/// <summary>
/// Optional extension implemented by state steppers that can derive an NPC spawn from the exact state
/// transition they just proposed. The executor asks for the intent before commit but applies it only after
/// the source generation-safe TryUpdate succeeds, preventing stale/retried AI from duplicating spawned NPCs.
/// </summary>
public interface INpcAiSpawnIntentPlanner
{
    bool TryPlanNpcSpawn(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        out NpcAiSpawnIntent intent);
}

/// <summary>Source-backed TerrariaServer 1.4.5.8 NewNPC lifecycle facts used by committed AI spawns.</summary>
public static class VanillaNpcSpawnFacts
{
    public const int ActiveTime = 750;
    public const int NewNpcTimeLeft = 937;
}

internal static class RuntimeNpcSpawnIntentApplier
{
    public static bool TryApply(
        RuntimeNpcStore npcs,
        in NpcAiSpawnIntent intent,
        out NpcSnapshot spawned)
    {
        ArgumentNullException.ThrowIfNull(npcs);

        if (!VanillaNpcDefinitionCatalog.TryGet(intent.Type, out VanillaNpcDefinition definition) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY))
        {
            spawned = default;
            return false;
        }

        var update = new NpcStateUpdate(
            Type: intent.Type.Value,
            NetId: checked((short)intent.Type.Value),
            PositionX: intent.BottomX - definition.Width * 0.5f,
            PositionY: intent.BottomY - definition.Height,
            VelocityX: intent.VelocityX,
            VelocityY: intent.VelocityY,
            Target: intent.Target,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft
            });

        return npcs.TrySpawnVanilla(in update, out spawned);
    }
}
