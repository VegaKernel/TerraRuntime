using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class GroundCombatCohort1458Tests
{
    [Fact]
    public void Source_verified_ground_combat_definitions_admit_exact_sixteen_identities()
    {
        // Initialized official 1458 SetDefaults readbacks. The existing positive
        // Armored Skeleton77 is a control, not a seventeenth new definition.
        (int Type, short Net, int Width, int Height, int Life, int Damage, int Defense)[] rows =
        [
            (185,185,28,28,70,26,12), (385,385,18,40,750,80,30),
            (482,482,28,48,110,30,18), (508,508,50,31,110,38,16),
            (580,580,26,26,80,25,10), (582,582,18,18,30,10,2),
            (590,590,18,40,45,14,6), (77,-15,20,44,400,50,32),
            (381,381,18,40,350,50,25), (382,382,18,40,350,50,25),
            (449,449,18,40,60,20,8), (450,450,18,40,55,20,12),
            (451,451,18,40,65,18,8), (452,452,18,40,60,22,8),
            (481,481,18,40,70,22,10), (520,520,34,110,2000,60,40)
        ];
        foreach (var row in rows)
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(row.Type),new(row.Net),out var definition));
            Assert.True(definition.SupportsRuntimeLifecycle, $"Creation-only identity {row.Net}");
            Assert.False(definition.CreationDefaultsOnly);
            Assert.Equal((row.Width,row.Height,row.Life,row.Damage,row.Defense),
                (definition.Width,definition.Height,definition.LifeMax,definition.Damage,definition.Defense));
            Assert.True(!VanillaNpcAiCoverageCatalog.TryGet(new(row.Type),out var coverage) || !coverage.FullVanillaAiParity);
        }
    }

    [Fact]
    public void Ground_combat_identity_membership_never_aliases_a_different_signed_parent()
    {
        foreach (int type in new[] {185,385,482,508,580,582,590,381,382,449,450,451,452,481,520})
        {
            Assert.True(VanillaGroundCombatCohort1458.IsNewDefinition(new(type),new((short)type)));
            Assert.True(VanillaGroundCombatCohort1458.IsSupported(new(type),new((short)type)));
        }
        Assert.True(VanillaGroundCombatCohort1458.IsNewDefinition(new(77),new(-15)));
        Assert.True(VanillaGroundCombatCohort1458.IsSupported(new(77),new(-15)));
        Assert.True(VanillaGroundCombatCohort1458.IsSupported(new(77),new(77)));
        Assert.False(VanillaGroundCombatCohort1458.IsNewDefinition(new(77),new(77)));
        Assert.False(VanillaGroundCombatCohort1458.IsSupported(new(3),new(-15)));
        Assert.False(VanillaGroundCombatCohort1458.IsSupported(new(77),new(-14)));
    }

    [Fact]
    public void Usual_unowned_attacks_children_and_transformations_keep_sibling_definitions_fenced()
    {
        foreach (int type in new[] {236,409,411,415,419,
            424,425,426,427,428,429,471,480,518,530})
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type),out var definition));
            Assert.True(definition.CreationDefaultsOnly,$"Unproved sibling {type}");
            Assert.False(definition.SupportsRuntimeLifecycle);
            Assert.False(VanillaGroundCombatCohort1458.IsSupported(new(type),new((short)type)));
        }
    }

    [Fact]
    public void Captured_raw_stealth_distinguishes_unknown_live_facts_from_source_constructor_defaults()
    {
        var constructor = VanillaNpcRawPlayer1458.Constructor(255);
        Assert.Equal(1f,constructor.Stealth);
        Assert.True(constructor.IsValid);
        Assert.True((constructor with {Stealth=null}).IsValid);
        Assert.False((constructor with {Stealth=float.NaN}).IsValid);
        Assert.False((constructor with {Stealth=float.PositiveInfinity}).IsValid);
        Assert.True((constructor with {Stealth=0f}).IsValid);
    }
}
