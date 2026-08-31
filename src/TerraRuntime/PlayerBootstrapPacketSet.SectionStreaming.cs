using TerraRuntime.World;

namespace TerraRuntime;

public sealed partial class PlayerBootstrapPacketSet
{
    /// <summary>World dimensions used by post-spawn packet-10 section streaming.</summary>
    internal WorldDimensions? StreamingDimensions => _world?.Header.Dimensions;

    /// <summary>Initial spawn sections already transferred before packet 49.</summary>
    internal ReadOnlySpan<WorldSectionId> BaseStreamingSections => _baseSections;
}
