using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryPrepareBossSummon(ConnectionHandle connection, int itemType,
        out EventSummonPreparation? preparation)
    {
        preparation = null;
        // Item.SetDefaults/ItemCheck_UseBossSpawners 1.4.5.8: represented single-root
        // night summons all consume one item and share useAnimation/useTime45.
        return itemType is 43 or 556 or 557 &&
            TryPreparePaidSummon(connection, itemType, 45, out preparation);
    }
}
