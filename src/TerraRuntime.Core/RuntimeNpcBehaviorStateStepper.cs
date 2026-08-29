using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Composes host-registered NPC state behaviors around the verified vanilla/default stepper while leaving the
/// authoritative store as the only state owner. Type-scoped pre/post decorators remain keyed by the vanilla
/// presentation ID, while an optional server-archetype BehaviorId can select a distinct replacement for one exact
/// generation even when multiple custom archetypes share that same presentation.
/// </summary>
public sealed class RuntimeNpcBehaviorStateStepper : INpcAiStateStepper
{
    private readonly INpcAiStateStepper vanilla;
    private readonly RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper> behaviors;
    private readonly IGameplayBehaviorFaultSink? faultSink;
    private readonly RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper>? archetypeBehaviors;
    private readonly RuntimeNpcArchetypeRegistry? archetypes;
    private readonly RuntimeNpcArchetypeIdentityStore? identities;

    public RuntimeNpcBehaviorStateStepper(
        INpcAiStateStepper vanilla,
        RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper> behaviors,
        IGameplayBehaviorFaultSink? faultSink = null,
        RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper>? archetypeBehaviors = null,
        RuntimeNpcArchetypeRegistry? archetypes = null,
        RuntimeNpcArchetypeIdentityStore? identities = null)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        ArgumentNullException.ThrowIfNull(behaviors);
        this.vanilla = vanilla;
        this.behaviors = behaviors;
        this.faultSink = faultSink;
        this.archetypeBehaviors = archetypeBehaviors;
        this.archetypes = archetypes;
        this.identities = identities;
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        RuntimeGameplayBehaviorSnapshot<NpcTypeId, INpcAiStateStepper> snapshot = behaviors.Snapshot;
        bool hasTypePlan = snapshot.TryGetPlan(
            npc.TypeIdentity,
            out GameplayBehaviorDispatchPlan<INpcAiStateStepper>? plan) &&
            plan is not null;
        bool hasArchetypeReplacement = TryResolveArchetypeReplacement(
            in npc,
            out GameplayBehaviorBinding<INpcAiStateStepper> archetypeReplacement);

        if (!hasTypePlan && !hasArchetypeReplacement)
            return vanilla.TryStepState(in npc, out next);

        NpcSnapshot current = npc;
        bool changed = false;

        ReadOnlySpan<GameplayBehaviorBinding<INpcAiStateStepper>> pre = plan is null
            ? ReadOnlySpan<GameplayBehaviorBinding<INpcAiStateStepper>>.Empty
            : plan.Pre.Span;
        for (int index = 0; index < pre.Length; index++)
        {
            GameplayBehaviorBinding<INpcAiStateStepper> binding = pre[index];
            if (!TryRunExtension(in binding, GameplayBehaviorStage.Pre, in current, out NpcStateUpdate update))
                continue;

            if (TryProjectExtensionUpdate(in current, in update, out NpcSnapshot projected))
            {
                current = projected;
                changed = true;
            }
            else
            {
                ReportInvalidResult(binding.Id, GameplayBehaviorStage.Pre);
            }
        }

        bool hasReplacement = hasArchetypeReplacement || (plan?.HasReplacement ?? false);
        if (hasReplacement)
        {
            GameplayBehaviorBinding<INpcAiStateStepper> replacement = hasArchetypeReplacement
                ? archetypeReplacement
                : plan!.Replacement;
            bool replacementReturned;
            bool replacementFaulted;
            NpcStateUpdate replacementUpdate;
            try
            {
                replacementReturned = replacement.Behavior.TryStepState(in current, out replacementUpdate);
                replacementFaulted = false;
            }
            catch (Exception exception)
            {
                replacementReturned = false;
                replacementFaulted = true;
                replacementUpdate = default;
                ReportFault(replacement.Id, GameplayBehaviorStage.Replacement, exception);
            }

            if (replacementReturned)
            {
                if (TryProjectExtensionUpdate(in current, in replacementUpdate, out NpcSnapshot projected))
                {
                    current = projected;
                    changed = true;
                }
                else
                {
                    ReportInvalidResult(replacement.Id, GameplayBehaviorStage.Replacement);
                    if (TryRunVanilla(in current, out projected, out NpcStateUpdate vanillaUpdate, out bool vanillaInvalid))
                    {
                        current = projected;
                        changed = true;
                    }
                    else if (vanillaInvalid)
                    {
                        next = vanillaUpdate;
                        return true;
                    }
                }
            }
            else if (replacementFaulted)
            {
                if (TryRunVanilla(in current, out NpcSnapshot projected, out NpcStateUpdate vanillaUpdate, out bool vanillaInvalid))
                {
                    current = projected;
                    changed = true;
                }
                else if (vanillaInvalid)
                {
                    next = vanillaUpdate;
                    return true;
                }
            }
        }
        else
        {
            if (TryRunVanilla(in current, out NpcSnapshot projected, out NpcStateUpdate vanillaUpdate, out bool vanillaInvalid))
            {
                current = projected;
                changed = true;
            }
            else if (vanillaInvalid)
            {
                next = vanillaUpdate;
                return true;
            }
        }

        ReadOnlySpan<GameplayBehaviorBinding<INpcAiStateStepper>> post = plan is null
            ? ReadOnlySpan<GameplayBehaviorBinding<INpcAiStateStepper>>.Empty
            : plan.Post.Span;
        for (int index = 0; index < post.Length; index++)
        {
            GameplayBehaviorBinding<INpcAiStateStepper> binding = post[index];
            if (!TryRunExtension(in binding, GameplayBehaviorStage.Post, in current, out NpcStateUpdate update))
                continue;

            if (TryProjectExtensionUpdate(in current, in update, out NpcSnapshot projected))
            {
                current = projected;
                changed = true;
            }
            else
            {
                ReportInvalidResult(binding.Id, GameplayBehaviorStage.Post);
            }
        }

        if (!changed)
        {
            next = default;
            return false;
        }

        next = ToUpdate(in current);
        return true;
    }

    private bool TryResolveArchetypeReplacement(
        in NpcSnapshot npc,
        out GameplayBehaviorBinding<INpcAiStateStepper> replacement)
    {
        if (archetypeBehaviors is not null &&
            archetypes is not null &&
            identities is not null &&
            identities.TryGet(npc.Handle, out GameplayArchetypeId archetypeId) &&
            archetypes.Snapshot.TryGet(archetypeId, out NpcArchetypeDescriptor descriptor) &&
            descriptor.BehaviorId.IsAssigned &&
            archetypeBehaviors.TryGetPublished(descriptor.BehaviorId, out replacement))
        {
            return true;
        }

        replacement = default;
        return false;
    }

    private bool TryRunExtension(
        in GameplayBehaviorBinding<INpcAiStateStepper> binding,
        GameplayBehaviorStage stage,
        in NpcSnapshot current,
        out NpcStateUpdate update)
    {
        try
        {
            return binding.Behavior.TryStepState(in current, out update);
        }
        catch (Exception exception)
        {
            ReportFault(binding.Id, stage, exception);
            update = default;
            return false;
        }
    }

    private bool TryRunVanilla(
        in NpcSnapshot current,
        out NpcSnapshot projected,
        out NpcStateUpdate vanillaUpdate,
        out bool vanillaInvalid)
    {
        if (!vanilla.TryStepState(in current, out vanillaUpdate))
        {
            projected = default;
            vanillaInvalid = false;
            return false;
        }

        if (IsValidState(in vanillaUpdate))
        {
            projected = Project(in current, in vanillaUpdate);
            vanillaInvalid = false;
            return true;
        }

        projected = default;
        vanillaInvalid = true;
        return false;
    }

    private static bool TryProjectExtensionUpdate(
        in NpcSnapshot current,
        in NpcStateUpdate update,
        out NpcSnapshot projected)
    {
        if (!IsValidState(in update))
        {
            projected = default;
            return false;
        }

        projected = Project(in current, in update);
        return true;
    }

    private static bool IsValidState(in NpcStateUpdate update) =>
        NpcTypeId.TryCreate(update.Type, out _) &&
        float.IsFinite(update.PositionX) &&
        float.IsFinite(update.PositionY) &&
        float.IsFinite(update.VelocityX) &&
        float.IsFinite(update.VelocityY) &&
        update.Ai.IsFinite &&
        update.Simulation.IsValid;

    private void ReportInvalidResult(GameplayExtensionId id, GameplayBehaviorStage stage) =>
        ReportFault(id, stage, new InvalidOperationException("Gameplay extension proposed an invalid NPC state transition."));

    private void ReportFault(GameplayExtensionId id, GameplayBehaviorStage stage, Exception exception)
    {
        if (faultSink is null)
            return;

        try
        {
            faultSink.BehaviorFaulted(id, stage, exception);
        }
        catch
        {
            // Diagnostics are observational and must never turn a contained extension fault into a runtime fault.
        }
    }

    private static NpcSnapshot Project(in NpcSnapshot current, in NpcStateUpdate update) =>
        new(
            current.Handle,
            current.Revision,
            update.Type,
            update.NetId,
            update.PositionX,
            update.PositionY,
            update.VelocityX,
            update.VelocityY,
            update.Target,
            update.Ai,
            update.Simulation);

    private static NpcStateUpdate ToUpdate(in NpcSnapshot snapshot) =>
        new(
            snapshot.Type,
            snapshot.NetId,
            snapshot.PositionX,
            snapshot.PositionY,
            snapshot.VelocityX,
            snapshot.VelocityY,
            snapshot.Target,
            snapshot.Ai,
            snapshot.Simulation);
}
