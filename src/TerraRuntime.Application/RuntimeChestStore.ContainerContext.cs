using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeChestContainerContext1458(
    ConnectionHandle Connection, short ChestId, bool Observed, ulong Revision);

internal sealed partial class RuntimeChestStore
{
    internal bool TryCaptureContainerContext(ConnectionHandle connection, out RuntimeChestContainerContext1458 capture)
    {
        capture = default;
        if (!connection.IsAssigned || craftingRevision == ulong.MaxValue) return false;
        int slot = connection.Player.Slot.Value;
        ConnectionHandle observed = activeConnections[slot];
        if (observed.IsAssigned && observed != connection) return false;
        capture = new(connection, observed.IsAssigned ? activeChests[slot] : NoChest,
            observed.IsAssigned, craftingRevision);
        return true;
    }

    internal bool IsCurrentContainerContext(in RuntimeChestContainerContext1458 capture) =>
        TryCaptureContainerContext(capture.Connection, out var current) && current == capture;

    private bool TryRecordOwnedContainerContext(ConnectionHandle connection, in TerrariaActiveChestState submitted,
        out bool closedWorldChest)
    {
        closedWorldChest = false;
        // Source GetCurrentContainer has four bank indices; unsupported negative
        // indices are not a guessed fifth bank or a new inventory permission.
        if (submitted.ChestId is < -5 or >= 0 || craftingRevision == ulong.MaxValue) return false;
        int slot = connection.Player.Slot.Value;
        if (activeConnections[slot].IsAssigned && activeConnections[slot] != connection) return false;
        short previous = activeChests[slot];
        if (previous >= 0 && owners[previous] == connection)
        {
            owners[previous] = default;
            closedWorldChest = true;
        }
        activeConnections[slot] = connection;
        activeChests[slot] = submitted.ChestId;
        craftingRevision++;
        return true;
    }
}
