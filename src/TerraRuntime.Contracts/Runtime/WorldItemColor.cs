namespace TerraRuntime.Contracts.Runtime;

/// <summary>Retained source item tint; packet 88 carries these RGBA bytes in this order.</summary>
public readonly record struct WorldItemColor(byte R, byte G, byte B, byte A);
