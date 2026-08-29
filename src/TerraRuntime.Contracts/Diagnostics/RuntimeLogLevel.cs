namespace TerraRuntime.Contracts.Diagnostics;

/// <summary>Severity of a structured TerraRuntime diagnostic event.</summary>
public enum RuntimeLogLevel : byte
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}
