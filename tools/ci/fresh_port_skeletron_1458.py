from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f"{label} anchor changed")
    return text.replace(old, new, 1)


def patch_content_ids() -> None:
    path = Path("src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs")
    text = path.read_text(encoding="utf-8")
    text = replace_once(
        text,
        "    public static readonly NpcTypeId Guide = new(22);\n    public static readonly NpcTypeId Demolitionist = new(38);",
        "    public static readonly NpcTypeId Guide = new(22);\n    public static readonly NpcTypeId SkeletronHead = new(35);\n    public static readonly NpcTypeId SkeletronHand = new(36);\n    public static readonly NpcTypeId Demolitionist = new(38);",
        "skeletron-npc-ids",
    )
    text = replace_once(
        text,
        "    public static readonly NpcAiStyleId Town = new(7);\n    public static readonly NpcAiStyleId KingSlime = new(15);",
        "    public static readonly NpcAiStyleId Town = new(7);\n    public static readonly NpcAiStyleId SkeletronHead = new(11);\n    public static readonly NpcAiStyleId SkeletronHand = new(12);\n    public static readonly NpcAiStyleId KingSlime = new(15);",
        "skeletron-ai-style-ids",
    )
    text = replace_once(
        text,
        "    public static readonly ProjectileTypeId ConfettiGun = new(178);\n    public static readonly ProjectileTypeId BloodShot = new(811);",
        "    public static readonly ProjectileTypeId ConfettiGun = new(178);\n    public static readonly ProjectileTypeId SkeletronSkull = new(270);\n    public static readonly ProjectileTypeId BloodShot = new(811);",
        "skeletron-projectile-id",
    )
    path.write_text(text, encoding="utf-8")


def patch_definitions() -> None:
    path = Path("src/TerraRuntime.Gameplay/Npcs/VanillaNpcDefinitionCatalog.cs")
    text = path.read_text(encoding="utf-8")
    text = replace_once(
        text,
        "    Vulture = 10,\n    SpikeBall = 11,\n    BlazingWheel = 12\n}",
        "    Vulture = 10,\n    SpikeBall = 11,\n    BlazingWheel = 12,\n    SkeletronHead = 13,\n    SkeletronHand = 14\n}",
        "behavior-family",
    )

    definitions = """        if (type == VanillaNpcIds.SkeletronHead)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.SkeletronHead,
                AiStyle: VanillaNpcAiStyles.SkeletronHead,
                BehaviorFamily: VanillaNpcBehaviorFamily.SkeletronHead,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 80,
                BaseHeight: 102,
                Damage: 32,
                Defense: 10,
                LifeMax: 4400,
                KnockBackResist: 0f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.SkeletronHand)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.SkeletronHand,
                AiStyle: VanillaNpcAiStyles.SkeletronHand,
                BehaviorFamily: VanillaNpcBehaviorFamily.SkeletronHand,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 52,
                BaseHeight: 52,
                Damage: 20,
                Defense: 14,
                LifeMax: 600,
                KnockBackResist: 0f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

"""
    text = replace_once(
        text,
        "        if (type == VanillaNpcIds.Skeleton)\n",
        definitions + "        if (type == VanillaNpcIds.Skeleton)\n",
        "skeletron-definitions",
    )
    path.write_text(text, encoding="utf-8")


def patch_stepper() -> None:
    path = Path("src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs")
    text = path.read_text(encoding="utf-8")
    text = replace_once(
        text,
        "    private readonly IVanillaNpcBehaviorStrategy _blazingWheel = new VanillaBlazingWheelNpcBehaviorStrategy();\n    private readonly IVanillaNpcRandom _random;",
        "    private readonly IVanillaNpcBehaviorStrategy _blazingWheel = new VanillaBlazingWheelNpcBehaviorStrategy();\n    private readonly VanillaSkeletronHeadNpcBehaviorStrategy _skeletronHead = new();\n    private readonly VanillaSkeletronHandNpcBehaviorStrategy _skeletronHand = new();\n    private readonly IVanillaNpcRandom _random;",
        "skeletron-fields",
    )
    text = replace_once(
        text,
        "            VanillaNpcBehaviorFamily.BlazingWheel => _blazingWheel,\n            _ => null",
        "            VanillaNpcBehaviorFamily.BlazingWheel => _blazingWheel,\n            VanillaNpcBehaviorFamily.SkeletronHead => _skeletronHead,\n            VanillaNpcBehaviorFamily.SkeletronHand => _skeletronHand,\n            _ => null",
        "skeletron-dispatch",
    )
    text = replace_once(
        text,
        "        if (NpcTypeId.TryCreate(source.Type, out NpcTypeId sourceType) &&\n            VanillaWormNpcCatalog.TryGet(sourceType, out _))",
        """        if (source.Type == VanillaNpcIds.SkeletronHead.Value &&
            proposed.Type == VanillaNpcIds.SkeletronHead.Value)
        {
            return PlanSkeletronHands(in source, in proposed, destination);
        }

        if (NpcTypeId.TryCreate(source.Type, out NpcTypeId sourceType) &&
            VanillaWormNpcCatalog.TryGet(sourceType, out _))""",
        "skeletron-spawn-dispatch",
    )

    planner = """    private int PlanSkeletronHands(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (destination.Length < 2 ||
            source.Ai.Ai0 != 0f ||
            proposed.Ai.Ai0 != 1f ||
            proposed.Type != source.Type ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHead, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        int spawnX = (int)(source.PositionX + hitbox.Width * 0.5f);
        int spawnY = (int)(source.PositionY + hitbox.Height * 0.5f);
        destination[0] = new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronHand,
            spawnX,
            spawnY,
            0f,
            0f,
            proposed.Target)
        {
            InitialAi = new NpcAiState(-1f, source.Handle.Slot, 0f, 0f)
        };
        destination[1] = new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronHand,
            spawnX,
            spawnY,
            0f,
            0f,
            proposed.Target)
        {
            InitialAi = new NpcAiState(1f, source.Handle.Slot, 0f, 150f)
        };
        return 2;
    }

"""
    text = replace_once(
        text,
        "    private int PlanWormFollower(\n",
        planner + "    private int PlanWormFollower(\n",
        "skeletron-planner",
    )
    path.write_text(text, encoding="utf-8")


def patch_target_slot() -> None:
    path = Path("src/TerraRuntime.Core/Npcs/VanillaSkeletronBehavior.cs")
    text = path.read_text(encoding="utf-8")
    text = replace_once(
        text,
        "            refresh.HasTarget &&\n            context.TryFindCandidate(refresh.Target, out target) &&",
        "            refresh.HasTarget &&\n            refresh.Target < byte.MaxValue &&\n            context.TryFindCandidate((byte)refresh.Target, out target) &&",
        "target-slot-narrowing",
    )
    path.write_text(text, encoding="utf-8")


def main() -> None:
    patch_content_ids()
    patch_definitions()
    patch_stepper()
    patch_target_slot()
    print("skeletron_fresh_port=ok")


if __name__ == "__main__":
    main()
