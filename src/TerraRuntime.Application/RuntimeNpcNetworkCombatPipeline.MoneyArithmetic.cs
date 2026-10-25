using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private static VanillaNpcMoneyContext1458 CreateMoneyContext(in NpcSnapshot npc,
        float value, int extraValue, float luck, bool midas, bool bloodMoon) =>
        new(value, extraValue, luck, midas, bloodMoon)
        {
            // The whole birth/update/strike/death captures prove this original-platform
            // arithmetic only for these explicit identities. Existing actor slices keep
            // their previous arithmetic until their Windows execution is independently verified.
            WindowsArithmetic = OperatingSystem.IsWindows() &&
                VanillaNpcGroundRoster1458.IsUndeadRuntimeIdentity(npc.TypeIdentity, npc.NetIdentity)
        };
}
