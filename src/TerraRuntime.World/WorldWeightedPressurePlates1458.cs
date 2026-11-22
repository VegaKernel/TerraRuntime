using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.World;

/// <summary>World-owned occupancy and saved keys of 1458 PressurePlateHelper.</summary>
public sealed class WorldWeightedPressurePlates1458
{
    private readonly WorldTileStore tiles;
    private readonly Dictionary<(int X, int Y), HashSet<PlayerHandle>?> pressed = [];
    private readonly (PlayerHandle Owner, float X, float Y)[] previous = new (PlayerHandle, float, float)[255];
    private bool needsFirstUpdate;

    internal WorldWeightedPressurePlates1458(WorldTileStore tiles, ReadOnlySpan<WorldPressurePlate> loaded, bool fromWorldFile)
    {
        this.tiles = tiles;
        foreach (var point in loaded) pressed.Add((point.X, point.Y), null);
        needsFirstUpdate = fromWorldFile;
    }

    public WorldPressurePlate[] CaptureSnapshot() => pressed.Keys.Select(p => new WorldPressurePlate(p.X, p.Y)).ToArray();

    public void FirstUpdate(Action<int, int> pulse)
    {
        if (!needsFirstUpdate) return;
        // Loaded occupants have no live player identities. Source releases each saved key once.
        foreach (var point in pressed.Keys.ToArray()) pulse(point.X, point.Y);
        pressed.Clear();
        needsFirstUpdate = false;
    }

    public void ResetPlayer(PlayerHandle player, Action<int, int> pulse)
    {
        if (!player.IsAssigned || player.Slot.Value >= previous.Length) return;
        foreach (var point in pressed.Keys.ToArray()) Leave(point, player, pulse);
    }

    public void Destroy(int x, int y, Action<int, int> pulse)
    {
        if (pressed.Remove((x, y))) pulse(x, y);
    }

    public bool Update(PlayerHandle player, float x, float y, int width, int height, Action<int, int> pulse)
    {
        if (!player.IsAssigned || player.Slot.Value >= previous.Length || !float.IsFinite(x) || !float.IsFinite(y) ||
            x is < -2147483000f or > 2147483000f || y is < -2147483000f or > 2147483000f ||
            width is <= 2 or > 512 || height is <= 2 or > 512) return false;
        var last = previous[player.Slot.Value];
        if (last.Owner.IsAssigned && last.Owner != player) ResetPlayer(last.Owner, pulse);
        float oldX = last.X, oldY = last.Y;
        var oldWindow = Window(oldX, oldY, width, height);
        var window = Window(x, y, width, height);
        foreach (var point in Cells(oldWindow))
        {
            var tile = tiles.Get(point.X, point.Y);
            if (tile.IsActive && tile.Type == WeightedPlate && !Intersects(x, y, width, height, point) && !Contains(window, point))
                Leave(point, player, pulse);
        }
        foreach (var point in Cells(window))
        {
            var tile = tiles.Get(point.X, point.Y);
            if (!tile.IsActive || tile.Type != WeightedPlate || !Intersects(x, y, width, height, point) ||
                Contains(oldWindow, point) && Intersects(oldX, oldY, width, height, point)) continue;
            if (pressed.TryGetValue(point, out var occupants))
            {
                if (occupants is null) pressed[point] = occupants = [];
                occupants.Add(player);
            }
            else
            {
                pressed.Add(point, [player]);
                pulse(point.X, point.Y);
            }
        }
        previous[player.Slot.Value] = (player, x, y);
        return true;
    }

    private const ushort WeightedPlate = 428;
    private void Leave((int X, int Y) point, PlayerHandle player, Action<int, int> pulse)
    {
        if (!pressed.TryGetValue(point, out var occupants)) return;
        occupants?.RemoveWhere(owner => owner.Slot == player.Slot);
        if (occupants?.Count > 0) return;
        pressed.Remove(point);
        pulse(point.X, point.Y);
    }

    private (int Left, int Top, int Right, int Bottom) Window(float x, float y, int width, int height) =>
        (Edge(x + 1, tiles.Dimensions.WidthTiles - 1), Edge(y + 1, Math.Max(0, tiles.Dimensions.HeightTiles - 40)),
         Edge(x + width - 1, tiles.Dimensions.WidthTiles - 1), Edge(y + height - 1, Math.Max(0, tiles.Dimensions.HeightTiles - 40)));
    private static int Edge(float value, int maximum) => (int)Math.Clamp(MathF.Floor(value / 16f), 0, maximum);
    private static bool Contains((int Left, int Top, int Right, int Bottom) window, (int X, int Y) point) =>
        point.X >= window.Left && point.X <= window.Right && point.Y >= window.Top && point.Y <= window.Bottom;
    private static IEnumerable<(int X, int Y)> Cells((int Left, int Top, int Right, int Bottom) window)
    {
        for (int x = window.Left; x <= window.Right; x++)
            for (int y = window.Top; y <= window.Bottom; y++) yield return (x, y);
    }
    private static bool Intersects(float x, float y, int width, int height, (int X, int Y) plate) =>
        (int)x + 1 < plate.X * 16 + 16 && (int)x + width - 1 > plate.X * 16 &&
        (int)y + 1 < plate.Y * 16 + 16 && (int)y + height - 1 > plate.Y * 16 + 6;
}
