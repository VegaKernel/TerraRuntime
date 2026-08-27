using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Narrow command-ingress view over the authoritative loop. Network and other producers depend on
/// the contract rather than on the loop implementation itself.
/// </summary>
public sealed class AuthoritativeCommandIngress<TState, TCommand> : IGameCommandIngress<TCommand>
    where TState : class
{
    private readonly AuthoritativeGameLoop<TState, TCommand> loop;

    public AuthoritativeCommandIngress(AuthoritativeGameLoop<TState, TCommand> loop)
    {
        this.loop = loop ?? throw new ArgumentNullException(nameof(loop));
    }

    public bool TryPost(GameCommandSourceId source, TCommand command) => loop.TryPost(source, command);
}
