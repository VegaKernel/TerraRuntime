using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Gameplay.Items;

public enum WiringToolKind1458 : byte { Wire, Actuator }

public readonly record struct WiringToolDefinition1458(ItemTypeId Item, WiringToolKind1458 Kind,
    byte Channel, byte PlaceAction, int UseTime, int UseAnimation, int TileBoost)
{
    public LocalItemClockDefinition1458 Clocks => new(UseTime, UseAnimation, 1, true, false, 0);
}

/// <summary>Item.SetDefaults and Player.ItemCheck_UseWiringTools, TerrariaServer1.4.5.8.</summary>
public static class VanillaWiringToolCatalog1458
{
    public const int WireItem = 530;
    public const int ActuatorItem = 849;

    public static bool TryGet(ItemTypeId item, out WiringToolDefinition1458 definition)
    {
        definition = item.Value switch
        {
            509 => new(item, WiringToolKind1458.Wire, 1, 5, 5, 15, 20),
            850 => new(item, WiringToolKind1458.Wire, 2, 10, 5, 15, 20),
            851 => new(item, WiringToolKind1458.Wire, 3, 12, 5, 15, 20),
            3612 => new(item, WiringToolKind1458.Wire, 4, 16, 5, 15, 20),
            ActuatorItem => new(item, WiringToolKind1458.Actuator, 0, 8, 10, 15, 0),
            _ => default
        };
        return definition.Item == item && !item.IsNone;
    }

    public static bool IsResource(ItemTypeId item) => item.Value is WireItem or ActuatorItem;
}
