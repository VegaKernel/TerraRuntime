public sealed class VanillaActor { public float X; public float Velocity; }
public static class OriginalMotion
{
    public static void Step(VanillaActor npc, float target)
    {
        if (npc.X < target)
            npc.Velocity += Main.rand.Next();
    }
}
