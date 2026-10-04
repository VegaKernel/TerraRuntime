using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeTownPlayerDanger1458(byte Slot,
    RuntimeTownPlayerBounds1458 Bounds, bool Dead, bool Stinky);

// One source scan must retain horizontal threat distances independently of attack handles.
// A closer unchaseable actor changes the distance without clearing an earlier attack slot.
internal readonly record struct RuntimeTownNpcDanger1458(bool Present, bool WithinRange,
    bool Stinky, float LeftDistance, float RightDistance, NpcSnapshot LeftAttack,
    NpcSnapshot RightAttack)
{
    internal int ThreatDirection => LeftDistance == -1f ? 1 :
        RightDistance == -1f ? -1 : RightDistance < -LeftDistance ? 1 : -1;
    internal float NearestHorizontalDistance => LeftDistance == -1f ? RightDistance :
        RightDistance > 0f && RightDistance < -LeftDistance ? RightDistance : -LeftDistance;
    internal NpcSnapshot AttackTarget => ThreatDirection == 1 ? RightAttack : LeftAttack;
}

internal static class RuntimeTownNpcDangerScanner1458
{
    internal static bool TryScan(WorldTileStore tiles, in NpcSnapshot source,
        ReadOnlySpan<NpcSnapshot> candidates, ReadOnlySpan<RuntimeTownPlayerDanger1458> players,
        RuntimeNpcStinkyStatus1458 status, bool activeTalk, bool meleeAttack,
        out RuntimeTownNpcDanger1458 facts)
    {
        facts = default;
        if (!TryCenter(in source, out float sx, out float sy)) return false;
        float attackRange = VanillaTownNpcDangerCatalog1458.DangerRange(source.Type);
        float scanRange = activeTalk && meleeAttack ? Math.Max(250f, attackRange) : attackRange;
        bool present = false, within = false, stinkyThreat = false;
        float left = -1f, right = -1f;
        NpcSnapshot leftAttack = default, rightAttack = default;
        // CopyActive is slot ordered. The future caller supplies its one live roster snapshot.
        foreach (NpcSnapshot candidate in candidates)
        {
            if (!candidate.IsActive || candidate.Handle.Slot >= 200 || candidate.Handle == source.Handle || candidate.Type == VanillaNpcIds.StatueMimic.Value ||
                VanillaTownNpcDangerCatalog1458.IsTurningCritter(candidate.Type)) continue;
            if (!TryCenter(in candidate, out float cx, out float cy)) return false;
            float dx = cx - sx, dy = cy - sy;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (!float.IsFinite(distance)) return false;
            if (distance >= scanRange) continue;
            if (!status.TryGetSourceOrderedStinky(source.Handle, candidate.Handle, out bool stinky)) return false;
            if (!VanillaNpcDefinitionCatalog.TryGet(candidate.TypeIdentity, candidate.NetIdentity,
                    out VanillaNpcDefinition definition) || candidate.Simulation.Friendly is null) return false;
            if ((candidate.Simulation.Friendly.Value ||
                    (candidate.Simulation.DamageOverride ?? definition.Damage) <= 0) && !stinky) continue;
            // SkeletonMerchant's Skeleton exclusion requires the exact source catalog before admission.
            if (source.Type == VanillaNpcIds.SkeletonMerchant.Value) return false;
            if (!candidate.Simulation.NoTileCollide &&
                !VanillaWorldCanHit.HasLineOfSight(tiles, sx, sy, 1, 1, cx, cy, 1, 1)) continue;
            present = true;
            if (distance >= attackRange) continue;
            within = true;
            stinkyThreat |= stinky;
            bool chaseable = VanillaNpcChaseability1458.CanBeChasedBy(in candidate);
            if (dx < 0f && (left == -1f || dx > left))
            {
                left = dx;
                if (chaseable) leftAttack = candidate;
            }
            if (dx > 0f && (right == -1f || dx < right))
            {
                right = dx;
                if (chaseable) rightAttack = candidate;
            }
        }
        if (!within && !activeTalk)
            foreach (RuntimeTownPlayerDanger1458 player in players)
            {
                if (player.Slot == byte.MaxValue || player.Dead || !player.Stinky) continue;
                float dx = player.Bounds.X + player.Bounds.Width * .5f - sx;
                float dy = player.Bounds.Y + player.Bounds.Height * .5f - sy;
                if (!(MathF.Sqrt(dx * dx + dy * dy) < attackRange)) continue;
                within = stinkyThreat = true;
                if (dx < 0f && (left == -1f || dx > left)) left = dx;
                if (dx > 0f && (right == -1f || dx < right)) right = dx;
            }
        facts = new(present, within, stinkyThreat, left, right, leftAttack, rightAttack);
        return true;
    }

    private static bool TryCenter(in NpcSnapshot npc, out float x, out float y)
    {
        x = y = 0f;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity,
                out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize size)) return false;
        x = npc.PositionX + size.Width * .5f;
        y = npc.PositionY + size.Height * .5f;
        return float.IsFinite(x) && float.IsFinite(y);
    }
}
