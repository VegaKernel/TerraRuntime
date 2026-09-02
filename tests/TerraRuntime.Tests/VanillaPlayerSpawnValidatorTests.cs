using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerSpawnValidatorTests
{
    [Fact]
    public void Accepts_vanilla_scalar_boundaries()
    {
        PlayerSpawnCommitRequest request = Request() with
        {
            SpawnX = -1,
            SpawnY = -1,
            Team = 5,
            SpawnContext = 3
        };

        Assert.True(VanillaPlayerSpawnValidator.IsValid(in request));
    }

    [Theory]
    [InlineData(-2, 0, 0, 0, 0, 0, 0)]
    [InlineData(0, -2, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, -1, 0, 0, 0)]
    [InlineData(0, 0, 0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, 0, 0, 6, 0)]
    [InlineData(0, 0, 0, 0, 0, 0, 4)]
    public void Rejects_invalid_scalar_ranges(
        short spawnX,
        short spawnY,
        int respawnTimer,
        short deathsPve,
        short deathsPvp,
        byte team,
        byte spawnContext)
    {
        PlayerSpawnCommitRequest request = new(
            new(0), spawnX, spawnY, respawnTimer, deathsPve, deathsPvp, team, spawnContext);

        Assert.False(VanillaPlayerSpawnValidator.IsValid(in request));
    }

    private static PlayerSpawnCommitRequest Request() =>
        new(new(0), 100, 200, 0, 0, 0, 0, 0);
}
