using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>NPC.SetDefaults / CanBeChasedBy, TerrariaServer 1.4.5.8 (normal debug options).
/// Type defaults are used only when materializing a verified definition, never in place of live instance state.</summary>
public static class VanillaNpcChaseability1458
{
    public static bool FriendlyAtSpawn(int type) => type is
        17 or 18 or 19 or 20 or 22 or 37 or 38 or 54 or 105 or 106 or 107 or 108 or 123 or 124 or
        142 or 160 or 178 or 207 or 208 or 209 or 227 or 228 or 229 or 353 or 354 or 357 or 368 or
        369 or 376 or 377 or 441 or 446 or 448 or 453 or 484 or 485 or 486 or 487 or 548 or 550 or
        579 or 588 or 589 or 606 or 633 or 637 or 638 or 656 or 663 or 670 or 678 or 679 or 680 or
        681 or 682 or 683 or 684 or 685 or 695 or 696;

    public static bool ChaseableAtSpawn(int type) => type is not (379 or 380 or 399 or 437 or 438 or 440 or 523);

    public static bool ImmortalAtSpawn(int type) => type is 488 or 690;

    public static bool CanBeChasedBy(in NpcSnapshot npc, bool ignoreDontTakeDamage = false) =>
        npc.IsActive && npc.Simulation.Chaseable == true && npc.Simulation.LifeMax > 5 &&
        (!npc.Simulation.DontTakeDamage || ignoreDontTakeDamage) &&
        npc.Simulation.Friendly == false && npc.Simulation.Immortal == false;
}
