using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryObserveClientWireRemoval(ClientTileManipulationRuntimeCommand command)
    {
        if (command.State.Action is not (6 or 9 or 11 or 13 or 17)) return false;
        ValidatedClientManipulations++;
        if (!TryApplyClientWireRemoval(command))
        {
            RejectedClientManipulations++;
            if (tiles is not null) replication?.TryPublishAuthoritativeCorrection(command.Connection.Source,
                tiles, command.State.TileX, command.State.TileY);
        }
        else AppliedClientManipulations++;
        return true;
    }

    private bool TryApplyClientWireRemoval(ClientTileManipulationRuntimeCommand command)
    {
        var request = command.State;
        int x = request.TileX, y = request.TileY;
        // Dedicated MessageBuffer17 runs the requested wire removal independently of the
        // client's ItemCheck/dust pool. It authenticates the sender and uses InWorld margin3;
        // it neither repeats the client's tool-priority selection nor simulates its dust RNG.
        if (tiles is null || !players.TryGet(command.Connection, out var member) ||
            !editBudget.TryConsume(command.Connection.Player.Slot) || x < 3 || y < 3 ||
            x >= tiles.Dimensions.WidthTiles - 3 || y >= tiles.Dimensions.HeightTiles - 3) return false;
        var player = member.CaptureSnapshot(); var before = tiles.Get(x, y);
        var kind = request.Action == 9 ? WorldWiringMutationKind.KillActuator : WorldWiringMutationKind.KillWire;
        var channel = request.Action switch { 6 => WorldWireChannel.Red, 11 => WorldWireChannel.Blue,
            13 => WorldWireChannel.Green, _ => WorldWireChannel.Yellow };
        var change = VanillaWorldWiringMutationService.Evaluate(new(kind, x, y, channel), in before);
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var world) || world is null) return false;
        using (world)
        {
            if (change.Applied)
            {
                if (!world.TryStageSourceWiringCell(x, y, before, change.After) ||
                    !world.TryBeginSourceDefaultItemOwner(command.Connection.Player.Slot.Value)) return false;
                try
                {
                    if (!world.TryStageSourceItemRectangle(new(request.Action == 9 ? 849 : 530), 1, x * 16, y * 16, 16, 16)) return false;
                }
                finally { world.EndSourceDefaultItemOwner(); }
            }
            var end = world.CapturePublicationOffset();
            if (!world.TrySeal() || !world.IsCurrentOwned || member.CaptureSnapshot() != player ||
                !players.TryGet(command.Connection, out var current) || !ReferenceEquals(member, current) ||
                !tiles.Get(x, y).Equals(before) || !world.TryAdoptWholeUnpublished(out var publication) || publication is null) return false;
            publication.TryPublishThrough(end);
            replication?.TryPublishCommitted(command.Connection.Source, request);
            return true;
        }
    }
}
