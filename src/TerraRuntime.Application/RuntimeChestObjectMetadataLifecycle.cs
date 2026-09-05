using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Runtime-owned chest metadata adapter for authoritative multi-tile transactions. The world layer owns geometry and
/// tile commits; this adapter owns slot allocation, coordinate identity, contents and open-session vetoes. Both sides
/// execute on the same game-thread writer. Commit methods still return a result so an invariant violation fails closed
/// before the world footprint changes instead of leaving tile and chest metadata split.
/// </summary>
internal sealed class RuntimeChestObjectMetadataLifecycle : IVanillaMultiTileObjectMetadataLifecycle
{
    private readonly RuntimeChestStore chests;

    public RuntimeChestObjectMetadataLifecycle(RuntimeChestStore chests) =>
        this.chests = chests ?? throw new ArgumentNullException(nameof(chests));

    public bool CanCreate(in VanillaMultiTileObjectMutationDescriptor descriptor) =>
        descriptor.MetadataKind == VanillaTileObjectMetadataKind.Chest &&
        chests.CanCreateAt(descriptor.TopLeftX, descriptor.TopLeftY);

    public bool CanRemove(in VanillaMultiTileObjectMutationDescriptor descriptor) =>
        descriptor.MetadataKind == VanillaTileObjectMetadataKind.Chest &&
        chests.CanRemoveAt(descriptor.TopLeftX, descriptor.TopLeftY);

    public bool TryCommitCreate(in VanillaMultiTileObjectMutationDescriptor descriptor) =>
        descriptor.MetadataKind == VanillaTileObjectMetadataKind.Chest &&
        chests.TryCreate(
            descriptor.TopLeftX,
            descriptor.TopLeftY,
            VanillaChestStorageFacts1458.DefaultItemSlots,
            out _);

    public bool TryCommitRemove(in VanillaMultiTileObjectMutationDescriptor descriptor) =>
        descriptor.MetadataKind == VanillaTileObjectMetadataKind.Chest &&
        chests.TryRemoveAt(
            descriptor.TopLeftX,
            descriptor.TopLeftY,
            out _);
}
