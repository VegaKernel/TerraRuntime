using System.Globalization;

namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Signed NPC net/persistence identity. This is deliberately distinct from <see cref="NpcTypeId"/>:
/// vanilla protocol variants may use a signed net id that maps back to a positive gameplay NPC type.
/// Version-specific mapping and validity remain owned by the protocol/source-backed adapter.
/// </summary>
public readonly record struct NpcNetId(int Value)
{
    public bool IsZero => Value == 0;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
