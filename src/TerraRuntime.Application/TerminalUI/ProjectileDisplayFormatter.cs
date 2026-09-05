using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Application.TerminalUI;

internal static class ProjectileDisplayFormatter
{
    public static string FormatOwner(byte spawner, ReadOnlySpan<RuntimePlayerSnapshot> players)
    {
        if (spawner == byte.MaxValue)
            return "server/world";

        for (int i = 0; i < players.Length; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            if (player.Slot == spawner)
                return $"owner {SanitizeName(player.Name)}(#{spawner})";
        }

        return $"owner slot #{spawner}";
    }

    public static string FormatType(int type) =>
        type switch
        {
            var value when value == VanillaProjectileIds.WoodenArrowFriendly.Value => $"WoodenArrowFriendly (#{type})",
            var value when value == VanillaProjectileIds.FireArrow.Value => $"FireArrow (#{type})",
            var value when value == VanillaProjectileIds.Shuriken.Value => $"Shuriken (#{type})",
            _ => $"projectile #{type}"
        };

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "<unnamed>";

        const int maximumLength = 20;
        int length = Math.Min(name.Length, maximumLength);
        char[] buffer = new char[length];
        for (int i = 0; i < length; i++)
            buffer[i] = char.IsControl(name[i]) ? ' ' : name[i];

        return new string(buffer);
    }
}
