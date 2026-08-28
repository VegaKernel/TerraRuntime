using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// World-geometry boundary for vanilla NPC AI visibility checks. Core owns AI ordering and state transitions;
/// the world layer owns Terraria tile traversal. Returning false means the authoritative tile query is blocked.
/// </summary>
public interface IVanillaNpcCanHitQuery
{
    bool CanHit(in NpcSnapshot npc, in VanillaNpcTargetCandidate target);
}
