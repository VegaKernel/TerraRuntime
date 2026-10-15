using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestStore
{
    private readonly bool ownsLoadedWorldStock;
    internal bool OwnsLoadedWorldStock => ownsLoadedWorldStock;

    // The trusted mode is selected by the authoritative world bootstrap, never
    // by a report. Its accepted slots remain fully owned; raw changes are staged.
    internal bool TryCaptureOwnedTransfer(ConnectionHandle connection, WorldTileStore tiles,
        out OwnedTransferCapture? capture)
    {
        capture = null;
        if (!ownsLoadedWorldStock || craftingRevision == ulong.MaxValue ||
            !TryGetOpenChest(connection, out WorldChest chest)) return false;
        capture = new(this, connection, chest, tiles, craftingRevision);
        return true;
    }

    internal sealed class OwnedTransferCapture
    {
        private readonly RuntimeChestStore owner;
        private readonly ConnectionHandle connection;
        private readonly WorldChest chest;
        private readonly WorldTileStore tiles;
        private readonly WorldTile anchor;
        private readonly WorldSectionId section;
        private readonly long sectionVersion;
        private readonly ulong revision;
        private bool adopted;
        private WorldChestItem[]? acceptedImage;

        internal OwnedTransferCapture(RuntimeChestStore owner, ConnectionHandle connection,
            WorldChest chest, WorldTileStore tiles, ulong revision)
        {
            this.owner = owner;
            this.connection = connection;
            this.chest = chest;
            this.tiles = tiles;
            this.revision = revision;
            Before = chest.Items.ToArray();
            anchor = tiles.Get(chest.X, chest.Y);
            section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, chest.X, chest.Y);
            sectionVersion = tiles.GetSectionVersion(section);
        }

        internal short ChestId => chest.SlotId;
        internal ConnectionHandle Connection => connection;
        internal WorldChestItem[] Before { get; }
        internal bool IsCurrent => !adopted && revision < ulong.MaxValue &&
            owner.craftingRevision == revision && owner.ownsLoadedWorldStock &&
            owner.TryGetOpenChest(connection, out WorldChest current) && ReferenceEquals(current, chest) &&
            chest.Items.AsSpan().SequenceEqual(Before) &&
            tiles.GetSectionVersion(section) == sectionVersion && tiles.Get(chest.X, chest.Y).Equals(anchor);

        internal bool IsAcceptedImageCurrent => adopted && acceptedImage is not null &&
            owner.craftingRevision == revision + 1 &&
            owner.TryGetOpenChest(connection, out WorldChest current) && ReferenceEquals(current, chest) &&
            chest.Items.AsSpan().SequenceEqual(acceptedImage) &&
            tiles.GetSectionVersion(section) == sectionVersion && tiles.Get(chest.X, chest.Y).Equals(anchor);

        internal bool TryAdoptUnpublished(ReadOnlySpan<WorldChestItem> after)
        {
            if (!IsCurrent || after.Length != Before.Length) return false;
            acceptedImage = after.ToArray();
            after.CopyTo(chest.Items);
            owner.craftingRevision++;
            adopted = true;
            return true;
        }
    }
}
