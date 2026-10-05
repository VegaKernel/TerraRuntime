using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private TownStrikePlan? pendingTownStrike;

    private bool TryPlanTownStrike(in NpcSnapshot npc, int direction, out TownStrikePlan? plan)
    {
        plan = null;
        // Skeleton Merchant is isLikeATownNPC, but source townNPC is false.
        if (!VanillaTownNpcFacts1458.IsHousingEligible(npc.TypeIdentity) && npc.Type is not 37 and not 368)
            return true;
        if (direction is < -1 or > 1 || !npc.Ai.IsValidFor(npc.TypeIdentity)) return false;
        if (npc.Ai.Ai0 is 3f or 4f or 16f or 17f)
        {
            if (npc.Ai.Ai2 != (int)npc.Ai.Ai2 || npc.Ai.Ai2 < 0 || npc.Ai.Ai2 >= npcs.Capacity)
                return false;
            // Source also resets an active conversation peer. This single-actor transaction cannot
            // silently omit that second physical writer; inactive peer slots need no mutation.
            if (npcs.TryGetActive((byte)npc.Ai.Ai2, out _)) return false;
        }
        var before = random.SourceRandom.Clone(); var after = before.Clone();
        var ai = npc.Ai with { Ai0 = 1f, Ai1 = 300 + after.Next(300), Ai2 = 0f };
        plan = new(before, after, new(npc.Handle, npc.Revision, ai, direction, random.SourceRandom, before));
        return true;
    }

    private void PublishTownStrikeRandom(TownStrikePlan? plan)
    {
        if (plan is null) return;
        if (pendingDeathPlan is { } death)
        {
            if (!death.TryPublishStrikePrelude())
                throw new InvalidOperationException("Accepted town strike lost its source RNG checkpoint.");
        }
        else
        {
            if (!random.SourceRandom.HasSameState(plan.Before))
                throw new InvalidOperationException("Accepted town strike lost its source RNG owner.");
            random.SourceRandom.CopyStateFrom(plan.After);
        }
    }

    private sealed record TownStrikePlan(VanillaUnifiedRandom1458 Before,
        VanillaUnifiedRandom1458 After, NpcTownStrikeReaction1458 Reaction);
}
