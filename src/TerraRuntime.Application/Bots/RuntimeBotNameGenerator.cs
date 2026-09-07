namespace TerraRuntime.Application.Bots;

/// <summary>Human-readable vanilla-length names for operator-created fake players.</summary>
internal static class RuntimeBotNameGenerator
{
    private const int TerrariaMaximumPlayerNameLength = 20;

    private static readonly string[] GivenNames =
    [
        "Alex", "Mira", "Kira", "Nika", "Max", "Leo", "Iris", "Milo", "Rin", "Tess",
        "Nora", "Aria", "Lina", "Kai", "Theo", "Luna", "Vera", "Niko", "Maya", "Zoe",
        "Aiden", "Daria", "Yuki", "Sora", "Rhea", "Ezra", "Loki", "Faye", "Roxy", "Juno"
    ];

    private static readonly string[] Callsigns =
    [
        "Vega", "Nova", "Raven", "Rook", "Viper", "Ghost", "Ember", "Frost", "Atlas", "Orbit",
        "Astra", "Volt", "Echo", "Comet", "Pixel", "Saber", "Onyx", "Blitz", "Neon", "Drift",
        "Kestrel", "Mantis", "Cinder", "Quasar", "Vector", "Flux", "Pulse", "Helix", "Mako", "Nyx"
    ];

    private static readonly string[] Suffixes = ["Prime", "Zero", "One", "IX", "X", "47", "77", "99"];

    public static string Generate(Random random, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(isTaken);

        for (int attempt = 0; attempt < 96; attempt++)
        {
            string candidate = random.Next(4) switch
            {
                0 => GivenNames[random.Next(GivenNames.Length)],
                1 => Callsigns[random.Next(Callsigns.Length)],
                2 => $"{GivenNames[random.Next(GivenNames.Length)]}-{Callsigns[random.Next(Callsigns.Length)]}",
                _ => $"{Callsigns[random.Next(Callsigns.Length)]}-{Suffixes[random.Next(Suffixes.Length)]}"
            };
            candidate = Clamp(candidate);
            if (!isTaken(candidate))
                return candidate;
        }

        string stem = Callsigns[random.Next(Callsigns.Length)];
        for (int serial = 1; serial <= 9_999; serial++)
        {
            string suffix = $"-{serial:000}";
            string candidate = Clamp(stem, suffix.Length) + suffix;
            if (!isTaken(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Could not allocate a unique vanilla-length bot name.");
    }

    private static string Clamp(string value, int reservedLength = 0)
    {
        int maximum = TerrariaMaximumPlayerNameLength - reservedLength;
        return value.Length <= maximum ? value : value[..maximum].TrimEnd('-');
    }
}
