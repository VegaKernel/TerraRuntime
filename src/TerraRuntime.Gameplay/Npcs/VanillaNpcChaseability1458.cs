using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>NPC.SetDefaults / CanBeChasedBy, TerrariaServer 1.4.5.8 (normal debug options).
/// Type defaults are used only when materializing a verified definition, never in place of live instance state.</summary>
public static class VanillaNpcChaseability1458
{
    public static bool FriendlyAtSpawn(int type) =>
        VanillaNpcSourceDefaultRoster1458.TryGetCreationRow(new(type), out var source) && source.Friendly || type is
        17 or 18 or 19 or 20 or 22 or 37 or 38 or 46 or 54 or 105 or 106 or 107 or 108 or 123 or 124 or 614 or
        142 or 160 or 178 or 207 or 208 or 209 or 227 or 228 or 229 or 353 or 354 or 357 or 368 or
        369 or 376 or 377 or 441 or 446 or 448 or 453 or 484 or 485 or 486 or 487 or 548 or 550 or
        579 or 588 or 589 or 606 or 633 or 637 or 638 or 656 or 663 or 670 or 678 or 679 or 680 or
        681 or 682 or 683 or 684 or 685 or 695 or 696 or
        // Main.Initialize_TileAndNPCData1 initializes npcCatchable before NPC.SetDefaults makes
        // these AI007 critters friendly. A constructor-only Main table silently omits this tail.
        148 or 149 or 299 or 300 or 361 or 362 or 364 or 366 or 367 or 443 or 445 or 447 or
        538 or 539 or 593 or 602 or 608 or 610 or 616 or 617 or
        639 or 640 or 641 or 642 or 643 or 644 or 645 or 646 or 647 or 648 or 649 or 650 or 651 or 652;

    public static bool ChaseableAtSpawn(int type) => type is not (379 or 380 or 399 or 437 or 438 or 440 or 523);

    public static bool ImmortalAtSpawn(int type) => type is 488 or 690;

    public static bool CanBeChasedBy(in NpcSnapshot npc, bool ignoreDontTakeDamage = false) =>
        npc.IsActive && npc.Simulation.Chaseable == true && npc.Simulation.LifeMax > 5 &&
        (!npc.Simulation.DontTakeDamage || ignoreDontTakeDamage) &&
        npc.Simulation.Friendly == false && npc.Simulation.Immortal == false;
}
