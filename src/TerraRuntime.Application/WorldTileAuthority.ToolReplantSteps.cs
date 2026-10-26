using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolActionSteps1458
    {
        private bool TryReplantAfterTree(int x, int y, int direction,
            TerrariaPlaceObjectState? reported)
        {
            if (!CanStage || expectedObject is not null || direction is not (-1 or 1)) return Reject();
            int? preview = reported is { } receipt ? receipt.Random : null;
            // Source CanPlace always consumes its random choice, including a
            // failed footprint. A matching79 can retain a prior UI preview's
            // bounded choice without creating a preview history or item credit.
            if (!client.TryReplantTree(x, y, direction, out var local, preview)) return Reject();
            if (!local.Placed) return reported is null || Reject();
            if (reported is { } received && received != local.Report) return Reject();
            expectedObject = local.Report;
            foreach (var cut in local.CutReports.Span)
            {
                if (cut.Style != 0 || !AppendTile(cut.Action, cut.TileX, cut.TileY, cut.Data) ||
                    !TryServerKillTile(cut.TileX, cut.TileY, false, expectedCount)) return Reject();
            }
            // The receiver handles preceding17 cuts before79. It then performs
            // its own CanPlace draw and uses the reported random choice. A
            // source-denied server placement is still a valid attempted action.
            return server.TryReplantTree(x, y, direction, out _, local.Report.Random) || Reject();
        }
    }
}
