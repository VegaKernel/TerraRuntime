namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>TerrariaServer 1.4.5.8 HandleMovement / DrManFlyFlask Kill argument bounds.</summary>
public static class VanillaEclipseProjectileFacts1458
{
    public const int MinimumCollisionStride = 3;
    public const int MaximumCollisionStride = 16;
    // Projectile.HandleMovement: increment num5, then break when num5 > 300 (Projectile.cs:17840).
    public const int MaximumCollisionSubsteps = 300;
    public const float CollisionVelocityEpsilon = .0001f;
    public const int FlaskIntermediateBodySize = 55;
    public const int FlaskGoreArgumentCount = 5;
    public const int FlaskGoreMinimum = 435;
    public const int FlaskGoreMaximumExclusive = 438;
}
