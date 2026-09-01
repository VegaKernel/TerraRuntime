using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// One NPC spawn requested by a speculative AI transition. Coordinates use vanilla NewNPC semantics:
/// integer X/Y identify the spawned NPC bottom-center, while velocity and optional initial ai[] are applied after
/// SetDefaults. The intent must never mutate the NPC store until the source NPC state commit succeeds.
/// </summary>
public readonly record struct NpcAiSpawnIntent(
    NpcTypeId Type,
    int BottomX,
    int BottomY,
    float VelocityX,
    float VelocityY,
    ushort Target)
{
    public NpcAiState InitialAi { get; init; }

    /// <summary>Source-owned initial localAI state applied atomically with allocation.</summary>
    public NpcAiState InitialLocalAi { get; init; }

    /// <summary>
    /// After the child slot is allocated, write that slot into the committed source's ai[0].
    /// Used by vanilla linked chains whose follower identity cannot be known speculatively.
    /// </summary>
    public bool LinkSourceFollowerSlot { get; init; }
}

/// <summary>
/// Optional extension implemented by state steppers that can derive zero or more NPC spawns from the exact
/// state transition they just proposed. The destination is executor-owned scratch storage valid only for the
/// synchronous call. The returned count must be in the inclusive range 0..destination.Length.
///
/// Intents are speculative until the source generation-safe state update commits. After commit they are applied
/// in source order with vanilla-style best-effort slot allocation: a full NPC table may accept an earlier child
/// and reject a later one, but a stale/rejected source transition can never leak any child spawn.
/// </summary>
public interface INpcAiSpawnIntentPlanner
{
    int PlanNpcSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination);
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
            !float.IsFinite(intent.VelocityY) ||
            !intent.InitialAi.IsFinite ||
            !intent.InitialLocalAi.IsFinite)
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
            Ai: intent.InitialAi,
            Simulation: NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft,
                LocalAi = intent.InitialLocalAi
            });

        return npcs.TrySpawnVanilla(in update, out spawned);
    }
}
