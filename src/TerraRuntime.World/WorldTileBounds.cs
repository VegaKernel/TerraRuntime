namespace TerraRuntime.World;

public readonly record struct WorldTileBounds(int X, int Y, int Width, int Height)
{
    public int ExclusiveRight => checked(X + Width);

    public int ExclusiveBottom => checked(Y + Height);
}
