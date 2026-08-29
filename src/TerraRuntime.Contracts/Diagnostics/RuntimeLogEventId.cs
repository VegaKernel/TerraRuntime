namespace TerraRuntime.Contracts.Diagnostics;

/// <summary>Stable machine-readable event identifier. Values are allocated by subsystem ranges.</summary>
public readonly record struct RuntimeLogEventId(int Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
