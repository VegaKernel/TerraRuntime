using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Shared marker semantics from NPC.RedHatSkeletronAdjustmentsEnabled, TerrariaServer 1.4.5.8.</summary>
public static class VanillaSkeletronCombat
{
    public static bool HasRedHatAdjustments(NpcTypeId type, in NpcAiState ai, in NpcAiState localAi) =>
        ((type == VanillaNpcIds.SkeletronHead || type == VanillaNpcIds.WaterSphere) && ai.Ai3 == 1f) ||
        ((type == VanillaNpcIds.SkeletronHand || type == VanillaNpcIds.DarkCaster) && localAi.Ai3 == 1f);
}
