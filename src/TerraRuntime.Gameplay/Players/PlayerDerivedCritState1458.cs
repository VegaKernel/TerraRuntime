namespace TerraRuntime.Gameplay.Players;

/// <summary>Class crit values retained by Player.Update, including its selected-item contribution.</summary>
public readonly record struct PlayerDerivedCritState1458(int Melee, int Ranged, int Magic)
{
    public static PlayerDerivedCritState1458 SourceBaseline => new(4, 4, 4);
}
