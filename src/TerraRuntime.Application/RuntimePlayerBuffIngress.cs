using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record PlayerBuffTypesRuntimeCommand(
    ConnectionHandle Connection,
    PlayerBuffTypesCommitRequest Request) : RuntimeCommand;

internal interface IPlayerBuffNetworkIngress
{
    bool TryPost(ConnectionHandle connection, ReadOnlyMemory<BuffTypeId> buffTypes);
}

internal sealed class RuntimePlayerBuffNetworkIngress : IPlayerBuffNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimePlayerBuffNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, ReadOnlyMemory<BuffTypeId> buffTypes)
    {
        if (!connection.IsAssigned || buffTypes.Length > TerrariaPlayerBuffCodec1458.MaximumBuffs)
            return false;

        // The network decoder owns its array. Clone here so no caller can mutate a queued command after handoff.
        BuffTypeId[] owned = buffTypes.ToArray();
        for (int i = 0; i < owned.Length; i++)
        {
            if (owned[i] == VanillaBuffIds.None || !VanillaBuffIds.TryCreate(owned[i].Value, out _))
                return false;
        }

        var request = new PlayerBuffTypesCommitRequest(connection.Player.Slot, owned);
        return ingress.TryPost(connection.Source, new PlayerBuffTypesRuntimeCommand(connection, request));
    }
}
