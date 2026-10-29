using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerRuntimeState
{
    internal void BindNpcInteractionWorldInfoSource(
        Func<RuntimeWorldProgressionMutationSnapshot, byte[]?> prepareWorldInfo, Action worldStateAdopted) =>
        _runtime.Npcs.BindNpcInteractionWorldInfoSource(prepareWorldInfo, worldStateAdopted);
}
