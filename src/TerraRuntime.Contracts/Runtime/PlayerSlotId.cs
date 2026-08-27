namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Identifies a Terraria player slot independently from transport connection identity.
/// </summary>
public readonly record struct PlayerSlotId(byte Value)
{
    public override string ToString() => $"player:{Value}";
}
