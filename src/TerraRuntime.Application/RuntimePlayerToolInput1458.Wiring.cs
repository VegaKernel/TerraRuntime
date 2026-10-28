using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerToolInput1458
{
    internal RuntimePlayerToolInput1458(WiringToolDefinition1458 wiring) : this(default(ToolDefinition1458)) => WiringDefinition = wiring;
    internal WiringToolDefinition1458? WiringDefinition { get; }
    internal bool IsWiring => WiringDefinition is not null;
    internal PlayerEquipmentCommitRequest? WiringInventoryReport { get; private set; }
    internal WorldTileAuthority.ToolWorldReadPreparation? WiringReads { get; private set; }

    internal bool TryBindWiringReads(WorldTileAuthority.ToolWorldReadPreparation reads)
    {
        if (!IsWiring || !IsCurrent || !reads.IsCurrent || WiringReads is not null) return false;
        WiringReads = reads;
        return true;
    }

    internal bool TryObserveWiringInventory(in PlayerEquipmentCommitRequest report)
    {
        if (!IsWiring || !IsCurrent || WiringReads is not { IsCurrent: true } || report.Stack < 0 ||
            report.SlotId is < 0 or >= 58 || actor is null || report.PlayerSlot != actor.Connection.Player.Slot ||
            !RuntimePlayerInventoryItem.TryFromNormalized(in report, out var reported) || !reported.IsCanonical) return false;
        if (WiringInventoryReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        WiringInventoryReport = report;
        Revision++;
        return true;
    }
}
