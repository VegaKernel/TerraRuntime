namespace TerraRuntime.Application;

internal readonly record struct ToolTreeActor1458(byte Slot, bool Active, bool Dead,
    float X, float Y, int Width, int Height, int SelectedAxe, float Luck)
{
    internal bool? FlowerBoots { get; init; }
    internal bool? ZoneGraveyard { get; init; }
    internal bool? HasSeedTool { get; init; }
}

/// <summary>
/// Source Player.FindClosest over the transaction's captured/virtual player image.
/// The enclosing participant retains exact generation/currentness; a slot alone
/// is never an authority or a replacement-player attachment.
/// </summary>
internal static class VanillaToolTreeActors1458
{
    public static bool TrySelect(ReadOnlySpan<ToolTreeActor1458> actors,
        float sourceX, float sourceY, int sourceWidth, int sourceHeight, out ToolTreeActor1458 selected)
    {
        selected = default;
        if (actors.Length > byte.MaxValue || !float.IsFinite(sourceX) || !float.IsFinite(sourceY) ||
            sourceWidth < 0 || sourceHeight < 0) return false;
        bool anyActive = false;
        float best = -1f;
        int previousSlot = -1;
        foreach (var actor in actors)
        {
            if (actor.Slot == byte.MaxValue || actor.Slot <= previousSlot) return false;
            previousSlot = actor.Slot;
            if (!actor.Active) continue;
            if (actor.Width <= 0 || actor.Height <= 0 || actor.SelectedAxe < 0 ||
                !float.IsFinite(actor.X) || !float.IsFinite(actor.Y) || !float.IsFinite(actor.Luck)) return false;
            if (!anyActive) { selected = actor; anyActive = true; }
            if (actor.Dead) continue;
            // Source uses Manhattan distance and integer half-width/half-height,
            // not Vector2.Distance or a nearest-center squared metric.
            float distance = Math.Abs(actor.X + actor.Width / 2 - (sourceX + sourceWidth / 2)) +
                Math.Abs(actor.Y + actor.Height / 2 - (sourceY + sourceHeight / 2));
            if (!float.IsFinite(distance)) return false;
            if (best < 0f || distance < best) { best = distance; selected = actor; }
        }
        // An owned tool action must still have an exact actor; do not fabricate
        // Player0 constructor state for an empty runtime roster.
        return anyActive;
    }
}
