public sealed class VanillaActor { public float X; public float Velocity; public float Acceleration; }
public static class OriginalMotion
{
    public static void Step(VanillaActor npc, float target)
    {
        if (npc.X < target)
            npc.Acceleration += 0.5f;
        else
            npc.Velocity -= 0.25f;
    }
}
