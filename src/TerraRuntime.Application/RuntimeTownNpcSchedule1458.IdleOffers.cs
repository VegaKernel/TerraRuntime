using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    private bool TryPlanRealIdleOffers(in NpcSnapshot source, in NpcStateUpdate body,
        in RuntimeTownNpcDanger1458 danger, ReadOnlySpan<RuntimeTownPlayerConversation1458> conversations,
        ReadOnlySpan<RuntimeTownPlayerDanger1458> players, ReadOnlySpan<NpcSnapshot> peers, bool partyIsUp,
        out NpcStateUpdate next, out bool force)
    {
        next = body;
        force = false;
        if (body.Ai.Ai0 != 0f || danger.WithinRange || body.Simulation.Wet || body.VelocityY != 0f) return true;
        // Both branches are genuine social offers. A selected offer searches its source context and
        // suppresses later else-if offers even when that search finds no eligible actor.
        if (random.Next(300) == 0)
            return TryPlanSocialAdmission(in source, in body, peers, excludePets: false);
        if (random.Next(1800) == 0)
            return TryPlanSocialAdmission(in source, in body, peers, excludePets: true);
        if (random.Next(1200) == 0 && (source.TypeIdentity == VanillaNpcIds.PartyGirl || partyIsUp && VanillaTownNpcDangerCatalog1458.IsPartyAttackType(source.Type)))
        {
            TryPlanPlayerOffer(in body, players, conversations, state: 6f, timer: 300f,
                requireTalkVisibility: false, out next, out force);
            return true;
        }
        if (random.Next(600) == 0 && source.TypeIdentity == VanillaNpcIds.Tavernkeep)
        {
            TryPlanPlayerOffer(in body, players, conversations, state: 18f, timer: 300f,
                requireTalkVisibility: false, out next, out force);
            return true;
        }
        if (random.Next(1800) == 0)
        {
            next = body with { Ai = body.Ai with { Ai0 = 2f, Ai1 = 45 * (1 + random.Next(1)) } };
            force = true;
            return true;
        }
        if (random.Next(600) == 0 && source.TypeIdentity == VanillaNpcIds.Pirate)
        {
            next = body with { Ai = body.Ai with { Ai0 = 11f, Ai1 = 30 * (1 + random.Next(3)) } };
            force = true;
            return true;
        }
        if (random.Next(1200) == 0)
            TryPlanPlayerOffer(in body, players, conversations, state: 7f, timer: 220f,
                requireTalkVisibility: true, out next, out force);
        return true;
    }

    private bool TryPlanSocialAdmission(in NpcSnapshot source, in NpcStateUpdate body,
        ReadOnlySpan<NpcSnapshot> peers, bool excludePets)
    {
        // Source timer offer is sampled before the eligibility search.
        int multiplier = random.Next(2) == 0 ? 1 + random.Next(3) : 1 + random.Next(2);
        int sourceDuration = 420 * multiplier;
        foreach (NpcSnapshot peer in peers)
        {
            if (peer.Handle == source.Handle || peer.Handle.Slot >= TerrariaNpcTalkCodec.MaximumNpcSlots ||
                (!VanillaTownNpcFacts1458.TryGetHousingCategory(peer.TypeIdentity, out int category) &&
                    !VanillaTownNpcDangerCatalog1458.IsNonPersistentSocialPeerType(peer.Type)) ||
                excludePets && category == VanillaTownNpcFacts1458.PetHousingCategory || peer.VelocityY != 0f ||
                peer.Simulation.Wet || peer.Ai.Ai0 > 1f ||
                peer.Ai.Ai0 == 1f && (HasRememberedDoor(peer.Handle) || peer.Ai.Ai1 > 200f) ||
                !VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(peer.Simulation, out var hitbox)) continue;
            float sx = body.PositionX + GetWidth(source.TypeIdentity) * .5f;
            float sy = body.PositionY + GetHeight(source.TypeIdentity) * .5f;
            float tx = peer.PositionX + hitbox.Width * .5f, ty = peer.PositionY + hitbox.Height * .5f;
            float dx = tx - sx, dy = ty - sy;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance is < 100f and > 20f &&
                VanillaWorldCanHit.HasLineOfSight(tiles, sx, sy, 1, 1, tx, ty, 1, 1))
            {
                // A peer transaction/state3/4 or16/17 is deliberately unowned by this package.
                // Reject the entire speculative phase, rather than publish an invented conversation state.
                return false;
            }
        }
        return sourceDuration > 0;
    }

    private void TryPlanPlayerOffer(in NpcStateUpdate body, ReadOnlySpan<RuntimeTownPlayerDanger1458> players,
        ReadOnlySpan<RuntimeTownPlayerConversation1458> conversations, float state, float timer,
        bool requireTalkVisibility, out NpcStateUpdate next, out bool force)
    {
        next = body;
        force = false;
        float sx = body.PositionX + GetWidth(new NpcTypeId(body.Type)) * .5f;
        float sy = body.PositionY + GetHeight(new NpcTypeId(body.Type)) * .5f;
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            RuntimeTownPlayerBounds1458 bounds = default;
            bool eligible = false;
            if (requireTalkVisibility)
            {
                foreach (RuntimeTownPlayerConversation1458 player in conversations)
                    if (player.Slot == slot && player.CanBeTalkedTo) { bounds = player.Bounds; eligible = true; break; }
            }
            else
            {
                foreach (RuntimeTownPlayerDanger1458 player in players)
                    if (player.Slot == slot && !player.Dead) { bounds = player.Bounds; eligible = true; break; }
            }
            if (!eligible) continue;
            float dx = bounds.X + bounds.Width * .5f - sx, dy = bounds.Y + bounds.Height * .5f - sy;
            if (!(MathF.Sqrt(dx * dx + dy * dy) < 150f) ||
                !VanillaWorldLineOfSight.CanHitLine(tiles, sx, body.PositionY,
                    bounds.X + bounds.Width * .5f, bounds.Y)) continue;
            // Offer facing compares positions, maintenance compares centers; source keeps those distinct.
            int direction = body.PositionX < bounds.X ? 1 : -1;
            next = body with { Ai = body.Ai with { Ai0 = state, Ai1 = timer, Ai2 = slot },
                Simulation = body.Simulation with { DirectionX = direction } };
            force = true;
            return;
        }
    }

}
