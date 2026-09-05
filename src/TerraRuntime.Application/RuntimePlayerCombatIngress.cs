using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed record PlayerPvpToggleRuntimeCommand(
    ConnectionHandle Connection,
    bool Hostile) : RuntimeCommand;

internal sealed record PlayerTeamRuntimeCommand(
    ConnectionHandle Connection,
    byte Team) : RuntimeCommand;

internal sealed record ClientPlayerPvpHitRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaPlayerHurtState State) : RuntimeCommand;

internal interface IPlayerCombatNetworkIngress
{
    bool TryPostPvpToggle(ConnectionHandle connection, bool hostile);
    bool TryPostTeam(ConnectionHandle connection, byte team);
    bool TryPostPvpHit(ConnectionHandle connection, in TerrariaPlayerHurtState state);
}

internal sealed class RuntimePlayerCombatNetworkIngress : IPlayerCombatNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimePlayerCombatNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPostPvpToggle(ConnectionHandle connection, bool hostile) =>
        connection.IsAssigned && ingress.TryPost(connection.Source, new PlayerPvpToggleRuntimeCommand(connection, hostile));

    public bool TryPostTeam(ConnectionHandle connection, byte team) =>
        connection.IsAssigned && team <= 5 && ingress.TryPost(connection.Source, new PlayerTeamRuntimeCommand(connection, team));

    public bool TryPostPvpHit(ConnectionHandle connection, in TerrariaPlayerHurtState state) =>
        connection.IsAssigned && state.IsStructurallyValid && state.Pvp &&
        ingress.TryPost(connection.Source, new ClientPlayerPvpHitRuntimeCommand(connection, state));
}
