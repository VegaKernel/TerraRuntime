namespace TerraRuntime.Core;

/// <summary>
/// Source-backed vanilla NPC defaults required by authoritative lifecycle and AI bring-up.
/// Values are clean-room facts extracted from TerrariaServer 1.4.5.8 SetDefaults; behavior stays
/// independently implemented in TerraRuntime.
/// </summary>
public readonly record struct VanillaNpcDefinition(
    int Type,
    int AiStyle,
    int Width,
    int Height,
    int Damage,
    int Defense,
    int LifeMax,
    float KnockBackResist,
    float Scale);

/// <summary>
/// Initial verified slice of the Terraria 1.4.5.8 NPC defaults catalog.
/// Reference TerrariaServer.exe SHA-256:
/// d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// </summary>
public static class VanillaNpcDefinitionCatalog
{
    public const ushort DefaultTarget = byte.MaxValue;
    public const int DefaultTimeLeft = 750;

    public static bool TryGet(int type, out VanillaNpcDefinition definition)
    {
        definition = type switch
        {
            1 => new VanillaNpcDefinition(
                Type: 1,
                AiStyle: 1,
                Width: 24,
                Height: 18,
                Damage: 7,
                Defense: 2,
                LifeMax: 25,
                KnockBackResist: 1f,
                Scale: 1f),
            2 => new VanillaNpcDefinition(
                Type: 2,
                AiStyle: 2,
                Width: 30,
                Height: 32,
                Damage: 18,
                Defense: 2,
                LifeMax: 60,
                KnockBackResist: 0.8f,
                Scale: 1f),
            3 => new VanillaNpcDefinition(
                Type: 3,
                AiStyle: 3,
                Width: 18,
                Height: 40,
                Damage: 14,
                Defense: 6,
                LifeMax: 45,
                KnockBackResist: 0.5f,
                Scale: 1f),
            _ => default
        };

        return definition.Type != 0;
    }
}
