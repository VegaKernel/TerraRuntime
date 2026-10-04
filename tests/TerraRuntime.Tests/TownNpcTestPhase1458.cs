using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
internal sealed class TownNpcTestPhase1458
{
    private readonly RuntimeTownNpcSchedule1458 schedule;
    private readonly RuntimeNpcStinkyStatus1458 status;
    private readonly RuntimeTownNpcCombat1458 combat;
    internal TownNpcTestPhase1458(RuntimeTownNpcStateStore town, RuntimeNpcStore npcs, WorldTileStore tiles,
        RuntimeTownNpcSchedule1458 schedule, RuntimeTownNpcCombat1458? combat = null)
    {
        this.schedule = schedule;
        status = new RuntimeNpcStinkyStatus1458(npcs);
        this.combat = combat ?? new RuntimeTownNpcCombat1458(town, npcs, new RuntimeProjectileStore(), tiles,
            default, new RuntimeWorldProgressionMutations(), false, false);
    }
    internal RuntimeTownNpcCombatTickSummary1458 Tick(in RuntimeTownNpcScheduleConditions1458 conditions,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players,
        ReadOnlySpan<RuntimeTownPlayerConversation1458> conversations = default,
        ReadOnlySpan<RuntimeTownPlayerSeat1458> seatedPlayers = default)
    {
        status.BeginWorldTick();
        return schedule.Tick(in conditions, players, status, combat, conversations, seatedPlayers);
    }
}
