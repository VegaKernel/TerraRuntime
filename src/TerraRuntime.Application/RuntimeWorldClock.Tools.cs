namespace TerraRuntime.Application;

internal sealed partial class RuntimeWorldClock
{
    private WorldTileAuthority? toolTreeShakeOwner;

    internal void BindToolTreeShakeOwner(WorldTileAuthority owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (toolTreeShakeOwner is not null && !ReferenceEquals(toolTreeShakeOwner, owner))
            throw new InvalidOperationException("World clock already belongs to another tool world.");
        owner.BindToolClock(this);
        toolTreeShakeOwner = owner;
    }
}
