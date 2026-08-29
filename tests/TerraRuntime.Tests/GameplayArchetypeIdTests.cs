using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class GameplayArchetypeIdTests
{
    [Fact]
    public void Stable_id_is_opaque_and_ordinal()
    {
        var id = new GameplayArchetypeId("vega:minigames.ctf/red-guardian");

        Assert.True(id.IsAssigned);
        Assert.Equal("vega:minigames.ctf/red-guardian", id.Value);
        Assert.Equal(id.Value, id.ToString());
        Assert.True(new GameplayArchetypeId("A").CompareTo(new GameplayArchetypeId("a")) < 0);
    }

    [Fact]
    public void Default_id_is_unassigned()
    {
        GameplayArchetypeId id = default;

        Assert.False(id.IsAssigned);
        Assert.Equal(string.Empty, id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad id")]
    [InlineData("bad\tid")]
    [InlineData("bad\nid")]
    public void Invalid_ids_are_rejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new GameplayArchetypeId(value));
    }

    [Fact]
    public void Oversized_id_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameplayArchetypeId(new string('x', GameplayArchetypeId.MaxLength + 1)));
    }
}
