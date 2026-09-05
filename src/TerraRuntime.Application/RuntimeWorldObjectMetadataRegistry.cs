using System.Runtime.CompilerServices;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Runtime composition binding between one loaded world tile store and its runtime-owned object metadata lifecycle.
/// The weak key keeps independent worlds/tests isolated without making a process-global "current world" singleton.
/// Persistence composition owns the initial binding because it already receives the canonical tile and chest stores
/// before ServerRuntimeState is constructed.
/// </summary>
internal static class RuntimeWorldObjectMetadataRegistry
{
    private static readonly ConditionalWeakTable<WorldTileStore, Binding> Bindings = new();

    public static void Bind(WorldTileStore tiles, RuntimeChestStore chests)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(chests);

        Bindings.Remove(tiles);
        Bindings.Add(tiles, new Binding(new RuntimeChestObjectMetadataLifecycle(chests)));
    }

    public static bool TryGet(
        WorldTileStore tiles,
        out IVanillaMultiTileObjectMetadataLifecycle metadata)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (Bindings.TryGetValue(tiles, out Binding? binding))
        {
            metadata = binding.Metadata;
            return true;
        }

        metadata = null!;
        return false;
    }

    private sealed class Binding(IVanillaMultiTileObjectMetadataLifecycle metadata)
    {
        public IVanillaMultiTileObjectMetadataLifecycle Metadata { get; } =
            metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
}
