namespace TerraRuntime.Contracts.Runtime;

/// <summary>A bounded case-insensitive sandbox identity that cannot also be interpreted as a filesystem path.</summary>
public readonly record struct SandboxName : IComparable<SandboxName>
{
    public const int MaxLength = 64;
    private readonly string? value;

    public SandboxName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().ToLowerInvariant();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(normalized.Length, MaxLength);
        for (int i = 0; i < normalized.Length; i++)
        {
            char character = normalized[i];
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
            {
                throw new ArgumentException(
                    "Sandbox names may contain only ASCII letters, digits, '-' and '_'.",
                    nameof(value));
            }
        }

        this.value = normalized;
    }

    public string Value => value ?? string.Empty;
    public bool IsAssigned => value is not null;
    public int CompareTo(SandboxName other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value;
}
