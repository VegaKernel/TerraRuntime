namespace TerraRuntime.Contracts.Runtime;

/// <summary>Retained post-player-phase flags; an absent nullable projection is not known-clear.</summary>
public readonly record struct PlayerDebuffSnapshot1458(bool OnFire, bool OnFire2, bool Poisoned);
