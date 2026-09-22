using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

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

    /// <summary>Optional source-selected net identity applied by SetDefaultsFromNetId after NewNPC allocation.</summary>
    public NpcNetId? NetIdOverride { get; init; }

    /// <summary>Vanilla NewNPC.Start: ascending search includes this slot; reverse search excludes it.</summary>
    public byte StartSlot { get; init; }

    /// <summary>Source-owned initial localAI state applied atomically with allocation.</summary>
    public NpcAiState InitialLocalAi { get; init; }

    /// <summary>Source-owned eligibility for replacement when vanilla has no free spawn slot.</summary>
    public bool CanBeReplacedByOtherNpcs { get; init; }

    /// <summary>
    /// After the child slot is allocated, write that slot into the committed source's ai[0].
    /// Used by vanilla linked chains whose follower identity cannot be known speculatively.
    /// </summary>
    public bool LinkSourceFollowerSlot { get; init; }

    /// <summary>After allocation, retain the child slot in this source localAI element (0..3).</summary>
    public byte? LinkSourceLocalAiSlot { get; init; }
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
    /// <summary>
    /// Optional source-ordered initialization before the remaining AI. The accepted first state proposal must
    /// be side-effect-free: it is discarded, initialization commits without publication, unlinked children are
    /// allocated, and the state step runs once more with fresh peers. Only the final AI revision is published.
    /// Returning false keeps ordinary post-step spawn semantics. Scratch counts remain bounded by destination.
    /// </summary>
    bool TryPlanInitialization(in NpcSnapshot source, in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination, out NpcStateUpdate initialization, out int count)
    {
        initialization = default;
        count = 0;
        return false;
    }

    int PlanNpcSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination);
}
