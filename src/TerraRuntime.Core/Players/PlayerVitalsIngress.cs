using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public interface IPlayerHealthIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerHealthCommitRequest request);
}

public interface IPlayerManaIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerManaCommitRequest request);
}
