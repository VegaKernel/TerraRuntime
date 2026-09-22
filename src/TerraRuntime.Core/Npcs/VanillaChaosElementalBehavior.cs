using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>World-query boundary for TerrariaServer 1.4.5.8 AI_003 Chaos Elemental teleport placement.</summary>
public interface IVanillaChaosElementalEnvironment
{
    bool TryFindTeleportSpot(
        float npcPositionX,
        float npcPositionY,
        int targetTileX,
        int targetTileY,
        IVanillaNpcRandom random,
        out int tileX,
        out int tileY);
}

/// <summary>
/// Post-commit authoritative tail of NPC.AI_003_Fighters for Chaos Elemental. The common fighter strategy advances
/// the 180-tick stuck clock first; only a committed clock value may consume the teleport-search random stream.
/// </summary>
internal sealed class VanillaChaosElementalBehavior(IVanillaNpcRandom random)
{
    private readonly IVanillaNpcRandom random = random ?? throw new ArgumentNullException(nameof(random));
    private IVanillaChaosElementalEnvironment? environment;

    public void SetEnvironment(IVanillaChaosElementalEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public NpcSnapshot Complete(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (environment is null ||
            before.TypeIdentity != VanillaNpcIds.ChaosElemental ||
            committed.TypeIdentity != VanillaNpcIds.ChaosElemental ||
            committed.Ai.Ai3 < 180f ||
            committed.Target >= byte.MaxValue ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !environment.TryFindTeleportSpot(
                committed.PositionX,
                committed.PositionY,
                (int)target.CenterX / 16,
                (int)target.CenterY / 16,
                random,
                out int tileX,
                out int tileY))
        {
            return committed;
        }

        var update = new NpcStateUpdate(
            committed.Type,
            committed.NetId,
            tileX * 16f - 9f,
            tileY * 16f - 40f,
            committed.VelocityX,
            committed.VelocityY,
            committed.Target,
            committed.Ai with { Ai3 = -120f },
            committed.Simulation);
        return mutations.TryUpdateState(in committed, in update, out NpcSnapshot teleported) ? teleported : committed;
    }
}
