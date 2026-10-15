using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestCommandProcessor
{
    internal bool TryCaptureCraftContainerContext(ConnectionHandle connection,
        out RuntimeChestContainerContext1458 capture) => store.TryCaptureContainerContext(connection, out capture);

    internal bool IsCurrentCraftContainerContext(in RuntimeChestContainerContext1458 capture) =>
        store.IsCurrentContainerContext(in capture);
}
