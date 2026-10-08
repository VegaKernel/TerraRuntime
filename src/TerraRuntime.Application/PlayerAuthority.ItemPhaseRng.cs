using TerraRuntime.Core;

namespace TerraRuntime.Application;

// An explicit source world seed is distinct from an unavailable seed; zero is a valid owned value.
internal readonly record struct PlayerUpdateRandomSeed1458(int Value);

internal sealed partial class PlayerAuthority
{
    private VanillaUnifiedRandom1458? playerUpdateRandom;
    private RuntimePlayerUpdateWorld1458? playerUpdateWorld;

    internal void SetPlayerUpdateWorldFacts(in RuntimePlayerUpdateWorld1458 world)
    {
        if (!world.IsValid || worldTiles is null || world.MaxTilesX != worldTiles.Dimensions.WidthTiles ||
            world.MaxTilesY != worldTiles.Dimensions.HeightTiles)
            throw new ArgumentException("Player update world fields must describe the owned tile world.", nameof(world));
        if (playerUpdateWorld is { } existing && existing != world)
            throw new InvalidOperationException("Player update world fields are bound once per runtime session.");
        playerUpdateWorld = world;
    }

    internal void SetPlayerUpdateRandom(VanillaUnifiedRandom1458 random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (playerUpdateRandom is not null && !ReferenceEquals(playerUpdateRandom, random))
            throw new InvalidOperationException("The player update stream is bound once per runtime session.");
        // Main.SwapRandom("UpdatePlayers") retains one stream across all players and connection changes.
        // Binding the seed does not prove the cursor after an unrepresented player phase.
        playerUpdateRandom = random;
    }
}
