namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Accepts owned typed commands from external producers without exposing authoritative mutable state.
/// </summary>
public interface IGameCommandIngress<in TCommand>
{
    bool TryPost(GameCommandSourceId source, TCommand command);
}
