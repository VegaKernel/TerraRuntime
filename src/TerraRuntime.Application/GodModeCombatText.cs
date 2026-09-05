using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal static class GodModeCombatText
{
    private static readonly string[] Messages = ["MISS", "NOPE", "NOT TODAY", "NICE TRY"];

    public static string Select(PlayerHandle target, long tick)
    {
        ulong generation = target.Generation.Value;
        int index = (int)((unchecked((ulong)tick) + target.Slot.Value + generation) % (ulong)Messages.Length);
        return Messages[index];
    }
}
