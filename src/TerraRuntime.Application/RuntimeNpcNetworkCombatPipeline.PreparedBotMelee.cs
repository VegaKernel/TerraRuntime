using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    internal bool TryCaptureOrdinaryNonlethalMelee(NpcHandle target, PlayerHandle attacker,
        int maximumBaseDamage, int armorPenetration, float knockBack, int direction,
        out NpcSnapshot captured)
    {
        captured = default;
        if (!npcs.TryGet(target, out var current) || current.Revision.Value == ulong.MaxValue ||
            current.Simulation.DontTakeDamage || current.Simulation.Life <= 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(current.TypeIdentity, current.NetIdentity, out var definition) ||
            definition.DefinitionOnly || definition.IsBoss || definition.Role != NpcArchetypeRole.Ordinary ||
            VanillaEaterOfWorldsLifecycle.IsSegment(current.TypeIdentity) || IsDestroyerMember(current.TypeIdentity) ||
            IsPrimeMember(current.TypeIdentity) || current.TypeIdentity == VanillaNpcIds.WallOfFleshEye)
            return false;
        var maximum = new NpcDamageRequest(target, DamageSource.FromPlayerItem(attacker), maximumBaseDamage,
            armorPenetration, true, knockBack, direction);
        if (!VanillaNpcDamageResolver.TryResolve(current.Simulation.DefenseOverride ?? definition.Defense,
                in maximum, out _, out int possibleDamage) || possibleDamage >= current.Simulation.Life)
            return false;
        captured = current;
        return true;
    }

    internal bool TryStrikePreparedBotMelee(in NpcSnapshot target, in NpcDamageRequest request,
        Func<bool> current, Action<NpcSnapshot> adoptAndPublish)
    {
        if (request.Target != target.Handle || !npcs.TryGet(target.Handle, out var live) || live != target ||
            !current()) return false;
        var prelude = new NpcDamagePrelude1458(target, null, current, adoptAndPublish);
        return TryApplyServerStrike(in request, out _, prelude: prelude, drainPreparedNonlethalPublication: true);
    }
}
