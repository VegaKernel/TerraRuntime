using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private static bool RequiresOwnedProgressionStrike(NpcTypeId type)
    {
        if (VanillaNpcDefinitionCatalog.TryGet(type, out var definition) && definition.IsBoss)
            return true;

        // NPC.DoDeathEvents (1.4.5.8) owns progression flags, while linked non-boss actors
        // can share a root's life or change the encounter's vulnerability. Packet 28 provides
        // neither an owned collision nor a death entitlement, even after melee math resolves.
        return VanillaEaterOfWorldsLifecycle.IsSegment(type) || IsDestroyerMember(type) ||
            type == VanillaNpcIds.SkeletronHand || type == VanillaNpcIds.WallOfFleshEye ||
            type == VanillaNpcIds.TheHungry || type == VanillaNpcIds.TheHungryII ||
            type == VanillaNpcIds.PrimeCannon || type == VanillaNpcIds.PrimeSaw ||
            type == VanillaNpcIds.PrimeVice || type == VanillaNpcIds.PrimeLaser ||
            type == VanillaNpcIds.GolemHead || type == VanillaNpcIds.GolemHeadFree ||
            type == VanillaNpcIds.GolemFistLeft || type == VanillaNpcIds.GolemFistRight ||
            type == VanillaNpcIds.PlanteraHook || type == VanillaNpcIds.PlanteraTentacle ||
            type == VanillaNpcIds.BrainCreeper || type == VanillaNpcIds.LunaticCultistClone ||
            type == VanillaNpcIds.MoonLordHead || type == VanillaNpcIds.MoonLordHand ||
            type == VanillaNpcIds.MoonLordFreeEye ||
            // Source death cases: Clown shop unlock; Vortex/Stardust/Nebula/Solar pillars.
            type.Value is 109 or 422 or 493 or 507 or 517;
    }
}
