using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

// Prepared source state immediately before StrikeNPC, retained without an eager live revision.
// The enclosing source-phase owner validates its independent status/RNG dependencies again
// after lethal admission; this prevents partial life/counter adoption on a refused death plan.
internal sealed record NpcDamagePrelude1458(
    NpcSnapshot Before,
    NpcSimulationState Simulation,
    Func<bool> IsCurrent,
    Action<NpcSnapshot> PublishBeforeStrike);
