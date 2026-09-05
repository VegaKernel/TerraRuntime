using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

/// <summary>
/// Single-writer context carried only while the authoritative loop applies one client-originated projectile
/// mutation. RuntimeProjectileStore commits synchronously, so replication can preserve the exact inbound
/// ProjectileKey and exclude the originating connection without leaking protocol state into Core.
/// </summary>
internal sealed class RuntimeProjectileClientCommitContext
{
    private bool active;
    private GameCommandSourceId source;
    private TerrariaProjectileKeyState key;

    public IDisposable Enter(GameCommandSourceId commitSource, in TerrariaProjectileKeyState commitKey)
    {
        if (commitSource.IsSystem)
            throw new ArgumentException("Client projectile commit source must identify a connection.", nameof(commitSource));
        if (!commitKey.IsValid)
            throw new ArgumentOutOfRangeException(nameof(commitKey));
        if (active)
            throw new InvalidOperationException("Nested client projectile commit scopes are not supported.");

        source = commitSource;
        key = commitKey;
        active = true;
        return new Scope(this);
    }

    public bool TryGet(out GameCommandSourceId commitSource, out TerrariaProjectileKeyState commitKey)
    {
        if (!active)
        {
            commitSource = default;
            commitKey = default;
            return false;
        }

        commitSource = source;
        commitKey = key;
        return true;
    }

    private void Exit()
    {
        if (!active)
            throw new InvalidOperationException("Client projectile commit scope is not active.");

        active = false;
        source = default;
        key = default;
    }

    private sealed class Scope(RuntimeProjectileClientCommitContext owner) : IDisposable
    {
        private RuntimeProjectileClientCommitContext? owner = owner;

        public void Dispose()
        {
            RuntimeProjectileClientCommitContext? current = Interlocked.Exchange(ref owner, null);
            current?.Exit();
        }
    }
}
