using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Gameplay.Bots;

/// <summary>
/// TerrariaServer 1.4.5.8 NPC bodies admitted for operator-controlled NpcBot motion.
/// A preset is admitted only when TerraRuntime has both authoritative world physics and the verified
/// controlled-motion family needed by <c>RuntimeNpcActorIntentStateStepper</c>. The current admitted families are
/// verified ground fighters plus source-backed AI_002 flying-eye, AI_005 flyer pursuit and the ordinary pre-wander
/// AI_014 bat pursuit lane. Other hostile NPCs remain fail-closed until their controlled-motion family is verified
/// explicitly.
/// </summary>
public static class VanillaBotNpcPresetCatalog1458
{
    public static bool IsSupported(NpcTypeId type)
    {
        if (!type.IsAssigned ||
            !VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition) ||
            definition.Role == NpcArchetypeRole.Town || definition.IsBoss || definition.Damage <= 0 ||
            !VanillaNpcActorControlSupport1458.IsSupported(type))
        {
            return false;
        }

        return true;
    }
}
