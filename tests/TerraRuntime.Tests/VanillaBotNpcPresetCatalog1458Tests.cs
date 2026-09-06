using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaBotNpcPresetCatalog1458Tests
{
    [Theory]
    [MemberData(nameof(SupportedPresets))]
    public void Source_backed_controlled_motion_families_are_admitted(NpcTypeId type)
    {
        Assert.True(VanillaNpcActorControlSupport1458.IsSupported(type));
        Assert.True(VanillaBotNpcPresetCatalog1458.IsSupported(type));
    }

    [Theory]
    [MemberData(nameof(RejectedPresets))]
    public void Unsupported_or_non_ordinary_bodies_remain_fail_closed(NpcTypeId type)
    {
        Assert.False(VanillaBotNpcPresetCatalog1458.IsSupported(type));
    }

    public static TheoryData<NpcTypeId> SupportedPresets => new()
    {
        VanillaNpcIds.Zombie,
        VanillaNpcIds.Skeleton,
        VanillaNpcIds.DemonEye,
        VanillaNpcIds.EaterOfSouls,
        VanillaNpcIds.CaveBat
    };

    public static TheoryData<NpcTypeId> RejectedPresets => new()
    {
        VanillaNpcIds.BlueSlime,
        VanillaNpcIds.Merchant,
        VanillaNpcIds.EyeOfCthulhu
    };
}
