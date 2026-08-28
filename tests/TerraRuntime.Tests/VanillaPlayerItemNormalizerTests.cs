using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerItemNormalizerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(6195, 6195)]
    [InlineData(6196, 0)]
    [InlineData(-1, 3521)]
    [InlineData(-18, 3504)]
    [InlineData(-19, 3764)]
    [InlineData(-24, 3769)]
    [InlineData(-25, 3503)]
    [InlineData(-48, 3480)]
    [InlineData(-49, 0)]
    public void Net_ids_match_vanilla_net_defaults(short input, short expected) =>
        Assert.Equal(expected, VanillaPlayerItemNormalizer.NormalizeNetId(input));

    [Theory]
    [InlineData(6195, 6195)]
    [InlineData(-19, 3764)]
    public void Valid_net_ids_cross_into_typed_item_identity(short input, int expected)
    {
        Assert.True(VanillaPlayerItemNormalizer.TryNormalizeNetId(input, out ItemTypeId itemType));
        Assert.Equal(expected, itemType.Value);
    }

    [Fact]
    public void Invalid_net_id_does_not_cross_the_gameplay_boundary()
    {
        Assert.False(VanillaPlayerItemNormalizer.TryNormalizeNetId(6196, out _));
        Assert.False(VanillaPlayerItemNormalizer.TryNormalizeNetId(-49, out _));
    }

    [Fact]
    public void Empty_or_invalid_items_become_canonical_air()
    {
        PlayerEquipmentCommitRequest empty = Request(stack: 0, itemNetId: 1, flags: byte.MaxValue);
        PlayerEquipmentCommitRequest invalid = Request(stack: 1, itemNetId: 6196, flags: byte.MaxValue);

        PlayerEquipmentCommitRequest normalizedEmpty = VanillaPlayerItemNormalizer.Normalize(in empty);
        PlayerEquipmentCommitRequest normalizedInvalid = VanillaPlayerItemNormalizer.Normalize(in invalid);
        Assert.Equal((0, (byte)0, (short)0, (byte)0), Fields(normalizedEmpty));
        Assert.Equal((0, (byte)0, (short)0, (byte)0), Fields(normalizedInvalid));
        Assert.True(normalizedEmpty.TryGetCanonicalItemType(out ItemTypeId emptyType));
        Assert.True(emptyType.IsNone);
        Assert.True(normalizedInvalid.TryGetCanonicalItemType(out ItemTypeId invalidType));
        Assert.True(invalidType.IsNone);
    }

    [Fact]
    public void Relay_preserves_favorite_and_strips_transient_or_unknown_flags()
    {
        PlayerEquipmentCommitRequest request = Request(stack: 7, itemNetId: -19, flags: byte.MaxValue);

        Assert.False(request.TryGetCanonicalItemType(out _));
        PlayerEquipmentCommitRequest normalized = VanillaPlayerItemNormalizer.Normalize(in request);

        Assert.Equal((7, (byte)3, (short)3764, (byte)1), Fields(normalized));
        Assert.True(normalized.TryGetCanonicalItemType(out ItemTypeId itemType));
        Assert.Equal(3764, itemType.Value);
        Assert.Equal(3, normalized.PrefixId.Value);
    }

    [Fact]
    public void Nonempty_none_identity_is_not_canonical()
    {
        PlayerEquipmentCommitRequest request = Request(stack: 1, itemNetId: 0, flags: 0);

        Assert.False(request.TryGetCanonicalItemType(out _));
        PlayerEquipmentCommitRequest normalized = VanillaPlayerItemNormalizer.Normalize(in request);
        Assert.Equal((0, (byte)0, (short)0, (byte)0), Fields(normalized));
        Assert.True(normalized.TryGetCanonicalItemType(out ItemTypeId itemType));
        Assert.True(itemType.IsNone);
    }

    private static PlayerEquipmentCommitRequest Request(short stack, short itemNetId, byte flags) =>
        new(new(1), 0, stack, 3, itemNetId, flags);

    private static (short Stack, byte Prefix, short ItemNetId, byte Flags) Fields(
        PlayerEquipmentCommitRequest request) =>
        (request.Stack, request.Prefix, request.ItemNetId, request.ItemFlags);
}
