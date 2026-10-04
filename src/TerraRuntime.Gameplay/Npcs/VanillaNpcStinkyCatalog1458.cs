namespace TerraRuntime.Gameplay.Npcs;

/// <summary>NPCID.Sets.DebuffImmunitySets, verified with original SetDefaults for all 697 types.</summary>
public static class VanillaNpcStinkyCatalog1458
{
    public const int VerifiedNpcTypeCount = 697;
    private const int DungeonGuardian = 68;
    private const int BlazingWheel = 72;
    private const int Wraith = 82;
    private const int TheDestroyer = 134;
    private const int TheDestroyerBody = 135;
    private const int TheDestroyerTail = 136;
    private const int Probe = 139;
    private const int Reaper = 253;
    private const int DungeonSpirit = 288;
    private const int Poltergeist = 330;
    private const int ForceBubble = 384;
    private const int MartianSaucer = 392;
    private const int MartianSaucerTurret = 393;
    private const int MartianSaucerCannon = 394;
    private const int MartianSaucerCore = 395;
    private const int LunarTowerVortex = 422;
    private const int CultistBoss = 439;
    private const int CultistBossClone = 440;
    private const int CultistDragonHead = 454;
    private const int CultistDragonBody1 = 455;
    private const int CultistDragonBody2 = 456;
    private const int CultistDragonBody3 = 457;
    private const int CultistDragonBody4 = 458;
    private const int CultistDragonTail = 459;
    private const int PirateShip = 491;
    private const int PirateShipCannon = 492;
    private const int LunarTowerStardust = 493;
    private const int LunarTowerNebula = 507;
    private const int LunarTowerSolar = 517;
    private const int DD2LanePortal = 549;
    private const int FairyCritterPink = 583;
    private const int FairyCritterGreen = 584;
    private const int FairyCritterBlue = 585;
    private const int PirateGhost = 662;
    private const int BoundTownSlimePurple = 686;
    private const int BoundTownSlimeYellow = 687;
    private const int StatueMimic = 690;

    public static bool IsImmune(int type) => type is
        DungeonGuardian or BlazingWheel or Wraith or TheDestroyer or TheDestroyerBody or TheDestroyerTail or Probe or Reaper or DungeonSpirit or Poltergeist or ForceBubble or MartianSaucer or MartianSaucerTurret or MartianSaucerCannon or MartianSaucerCore or LunarTowerVortex or CultistBoss or CultistBossClone or CultistDragonHead or CultistDragonBody1 or CultistDragonBody2 or CultistDragonBody3 or CultistDragonBody4 or CultistDragonTail or PirateShip or PirateShipCannon or LunarTowerStardust or LunarTowerNebula or LunarTowerSolar or DD2LanePortal or FairyCritterPink or FairyCritterGreen or FairyCritterBlue or PirateGhost or BoundTownSlimePurple or BoundTownSlimeYellow or StatueMimic;
}
