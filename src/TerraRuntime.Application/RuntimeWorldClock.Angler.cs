namespace TerraRuntime.Application;

internal sealed partial class RuntimeWorldClock
{
    private PlayerAuthority? anglerOwner;

    internal void BindAnglerOwner(PlayerAuthority owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (anglerOwner is not null && !ReferenceEquals(anglerOwner, owner))
            throw new InvalidOperationException("World clock already belongs to another Angler world.");
        anglerOwner = owner;
    }

    private void ResetAnglerQuestAtDawn() => anglerOwner?.SwapAnglerQuestAtDawn();
}
