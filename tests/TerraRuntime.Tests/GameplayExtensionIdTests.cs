using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class GameplayExtensionIdTests
{
    [Fact]
    public void Stable_id_preserves_exact_ordinal_value()
    {
        var id = new GameplayExtensionId("vega:minigames.ctf/projectile");

        Assert.True(id.IsAssigned);
        Assert.Equal("vega:minigames.ctf/projectile", id.Value);
        Assert.Equal(id.Value, id.ToString());
    }

    [Fact]
    public void Default_id_is_unassigned()
    {
        GameplayExtensionId id = default;

        Assert.False(id.IsAssigned);
        Assert.Equal(string.Empty, id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vega:bad id")]
    [InlineData("vega:bad\tid")]
    [InlineData("vega:bad\nid")]
    public void Invalid_ids_are_rejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new GameplayExtensionId(value));
    }

    [Fact]
    public void Oversized_id_is_rejected()
    {
        string value = new('x', GameplayExtensionId.MaxLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayExtensionId(value));
    }

    [Fact]
    public void Comparison_is_ordinal_and_deterministic()
    {
        var upper = new GameplayExtensionId("A");
        var lower = new GameplayExtensionId("a");

        Assert.True(upper.CompareTo(lower) < 0);
    }
}
