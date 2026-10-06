using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaPirateGhostMotion1458(float VelocityX, float VelocityY, int Alpha, NpcAiState LocalAi);

/// <summary>Verified AI122 defaults and deterministic motion; death remains an authoritative transaction.</summary>
public static class VanillaPirateGhostNpcCatalog1458
{
    public static readonly VanillaNpcDefinition Definition = new(
        VanillaNpcIds.PirateGhost, VanillaNpcAiStyles.PirateGhost, VanillaNpcBehaviorFamily.PirateGhost,
        VanillaNpcPhysicsFamily.NoClipFlight, NpcArchetypeRole.Ordinary,
        18, 40, 75, 22, 500, .2f, 1f, true, true, VanillaNpcSyncAnchor.TopLeft) { DefinitionOnly = true, SourceHitboxAtSpawn = new(18, 40) };

    public static bool TryMove(in NpcSnapshot actor, in VanillaNpcHitboxSize body,
        VanillaNpcTargetCandidate? target, ReadOnlySpan<NpcSnapshot> peers,
        out VanillaPirateGhostMotion1458 next, out bool fadeStrike)
    {
        next = default;
        fadeStrike = false;
        if (actor.TypeIdentity != VanillaNpcIds.PirateGhost || !body.IsValid ||
            !float.IsFinite(actor.PositionX) || !float.IsFinite(actor.PositionY) ||
            !float.IsFinite(actor.VelocityX) || !float.IsFinite(actor.VelocityY))
            return false;
        float vx = actor.VelocityX, vy = actor.VelocityY;
        int alpha = actor.Simulation.Alpha;
        if (target is not { } aimed)
        {
            vx *= .9f;
            vy *= .9f;
            alpha = Math.Min(255, alpha + 5);
            fadeStrike = alpha == 255;
        }
        else
        {
            alpha = Math.Max(0, alpha - 5);
            float centerX = actor.PositionX + body.Width * .5f;
            float centerY = actor.PositionY + body.Height * .5f;
            float wantedX = 0f, wantedY = 0f;
            MoveTowards(ref wantedX, ref wantedY, aimed.CenterX - centerX, aimed.CenterY - centerY, 4f);
            MoveTowards(ref vx, ref vy, wantedX, wantedY, 2f / 15f);
            // Slot order is semantically observable through Single rounding; callers supply source ascending peers.
            int previousSlot = -1;
            foreach (var peer in peers)
            {
                if (!peer.IsActive || peer.TypeIdentity != actor.TypeIdentity || peer.Handle == actor.Handle)
                    continue;
                if (peer.Handle.Slot <= previousSlot ||
                    !Definition.TryResolveHitbox(peer.Simulation, out var peerBody))
                    return false;
                previousSlot = peer.Handle.Slot;
                float dx = peer.PositionX + peerBody.Width * .5f - centerX;
                float dy = peer.PositionY + peerBody.Height * .5f - centerY;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (!float.IsFinite(distance)) return false;
                if (distance >= 50f) continue;
                // The original Normalize produces NaN at coincident centers. Reject the entire owned plan.
                if (distance == 0f) return false;
                float inverse = 1f / distance;
                dx *= inverse;
                dy *= inverse;
                dx *= .1f;
                dy *= .1f;
                vx -= dx;
                vy -= dy;
                vx -= dx;
            }
        }
        if (!float.IsFinite(vx) || !float.IsFinite(vy)) return false;
        var local = actor.Simulation.LocalAi;
        if (local.Ai0 == 0f) local = local with { Ai0 = 1f };
        next = new(vx, vy, alpha, local);
        return true;
    }

    private static void MoveTowards(ref float x, ref float y, float targetX, float targetY, float maximum)
    {
        float dx = targetX - x, dy = targetY - y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < maximum)
        {
            x = targetX;
            y = targetY;
            return;
        }
        float inverse = 1f / length;
        dx *= inverse;
        dy *= inverse;
        x += dx * maximum;
        y += dy * maximum;
    }
}
