using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Tests;

public sealed class VanillaBuffPrefixCatalogTests
{
    [Fact]
    public void Buff_catalog_covers_exact_1458_identity_range()
    {
        Assert.Equal(401, VanillaBuffIds.Count);
        Assert.Equal(VanillaBuffIds.Count, VanillaBuffDefinitionCatalog.Count);

        for (int rawType = 0; rawType < VanillaBuffIds.Count; rawType++)
        {
            Assert.True(VanillaBuffIds.TryCreate(rawType, out BuffTypeId type));
            Assert.True(VanillaBuffDefinitionCatalog.TryGet(type, out VanillaBuffDefinition definition));
            Assert.Equal(rawType, definition.Type.Value);
        }

        Assert.False(VanillaBuffIds.TryCreate(-1, out _));
        Assert.False(VanillaBuffIds.TryCreate(VanillaBuffIds.Count, out _));
        Assert.False(VanillaBuffDefinitionCatalog.TryGet(new BuffTypeId(VanillaBuffIds.Count), out _));
        Assert.True(VanillaBuffDefinitionCatalog.TryGet(VanillaBuffIds.None, out VanillaBuffDefinition none));
        Assert.False(none.IsPresent);
    }

    [Fact]
    public void Buff_definition_composes_selected_source_backed_sets()
    {
        Assert.True(VanillaBuffDefinitionCatalog.TryGet(VanillaBuffIds.WellFed, out VanillaBuffDefinition wellFed));
        Assert.True(wellFed.IsWellFed);
        Assert.True(wellFed.IsFedState);
        Assert.False(wellFed.IsFlaskBuff);

        Assert.True(VanillaBuffDefinitionCatalog.TryGet(VanillaBuffIds.Starving, out VanillaBuffDefinition starving));
        Assert.False(starving.IsWellFed);
        Assert.True(starving.IsFedState);

        Assert.True(VanillaBuffDefinitionCatalog.TryGet(
            VanillaBuffIds.WeaponImbueIchor,
            out VanillaBuffDefinition flask));
        Assert.True(flask.IsFlaskBuff);

        Assert.True(VanillaBuffDefinitionCatalog.TryGet(VanillaBuffIds.Frostburn2, out VanillaBuffDefinition frostburn));
        Assert.True(frostburn.TimeIsExtendedWithGameDifficulty);
        Assert.True(VanillaBuffDefinitionCatalog.TryGet(VanillaBuffIds.Shimmer, out VanillaBuffDefinition shimmer));
        Assert.False(shimmer.TimeIsExtendedWithGameDifficulty);
    }

    [Fact]
    public void Prefix_catalog_covers_exact_1458_identity_range_and_named_summon_metadata()
    {
        Assert.Equal(98, VanillaPrefixIds.Count);
        Assert.Equal(VanillaPrefixIds.Count, VanillaItemPrefixCatalog.Count);

        for (int rawType = 0; rawType < VanillaPrefixIds.Count; rawType++)
        {
            Assert.True(VanillaPrefixIds.TryCreate(rawType, out PrefixId type));
            Assert.True(VanillaItemPrefixCatalog.TryGetDefinition(
                type,
                out VanillaPrefixDefinition definition));
            Assert.Equal(rawType, definition.Type.Value);
        }

        Assert.False(VanillaPrefixIds.TryCreate(-1, out _));
        Assert.False(VanillaPrefixIds.TryCreate(VanillaPrefixIds.Count, out _));
        Assert.False(VanillaItemPrefixCatalog.TryGetDefinition(
            new PrefixId(VanillaPrefixIds.Count),
            out _));

        Assert.True(VanillaItemPrefixCatalog.TryGetDefinition(
            VanillaPrefixIds.Fabled,
            out VanillaPrefixDefinition fabled));
        Assert.True(fabled.IsSummonRollable);
        Assert.False(fabled.HasReducedNaturalChance);
        Assert.True(fabled.IsPresent);

        Assert.True(VanillaItemPrefixCatalog.TryGetDefinition(
            VanillaPrefixIds.Broken,
            out VanillaPrefixDefinition broken));
        Assert.True(broken.IsSummonRollable);
        Assert.True(broken.HasReducedNaturalChance);
    }
}
