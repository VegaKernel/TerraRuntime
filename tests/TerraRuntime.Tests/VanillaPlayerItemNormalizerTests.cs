using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerItemNormalizerTests
{
    [Fact]
    public void Source_backed_item_stack_above_verified_maximum_normalizes_to_empty()
    {
        var request = new PlayerEquipmentCommitRequest(
            new PlayerSlotId(0),
            SlotId: 0,
            Stack: 10_000,
            Prefix: 0,
            ItemNetId: checked((short)VanillaItemIds.DirtBlock.Value),
            ItemFlags: 0);

        PlayerEquipmentCommitRequest normalized = VanillaPlayerItemNormalizer.Normalize(in request);

        Assert.Equal((short)0, normalized.Stack);
        Assert.Equal((short)0, normalized.ItemNetId);
    }

    [Fact]
    public void Positive_stack_for_unimported_canonical_item_remains_compatible()
    {
        PlayerEquipmentCommitRequest request = Request(
            stack: short.MaxValue,
            itemNetId: 1,
            flags: 0);

        PlayerEquipmentCommitRequest normalized = VanillaPlayerItemNormalizer.Normalize(in request);

        Assert.Equal(short.MaxValue, normalized.Stack);
        Assert.Equal((short)1, normalized.ItemNetId);
    }

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

        Assert.Equal(
            (7, (byte)3, (short)3764, VanillaPlayerItemNormalizer.FavoriteItemFlag),
            Fields(normalized));
        Assert.True(normalized.TryGetCanonicalItemType(out ItemTypeId itemType));
        Assert.Equal(3764, itemType.Value);
        Assert.Equal(3, normalized.PrefixId.Value);
    }

    [Fact]
    public void Unknown_prefix_normalizes_to_named_none()
    {
        PlayerEquipmentCommitRequest request = Request(stack: 1, itemNetId: 1, flags: 0) with
        {
            Prefix = checked((byte)VanillaPrefixIds.Count)
        };

        PlayerEquipmentCommitRequest normalized = VanillaPlayerItemNormalizer.Normalize(in request);

        Assert.Equal(VanillaPrefixIds.NoneValue, normalized.Prefix);
        Assert.Equal(VanillaPrefixIds.None, normalized.PrefixId);
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
