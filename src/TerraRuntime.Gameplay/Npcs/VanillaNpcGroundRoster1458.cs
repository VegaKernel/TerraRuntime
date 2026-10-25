using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Complete NPC.SetDefaults/SetDefaultsFromNetId AI003/AI007 definition roster for 1.4.5.8.
/// Reuses the independently captured classic metadata; definition admission does not select AI.
/// </summary>
public static class VanillaNpcGroundRoster1458
{
    public const int FighterTypeCount = 191;
    public const int FighterVariantCount = 27;
    public const int TownStyleTypeCount = 82;

    /// <summary>Exact independently verified undead lifecycle cohort; no style-wide admission.</summary>
    public static bool IsUndeadRuntimeIdentity(NpcTypeId type, NpcNetId netId) => netId.Value switch
    {
        -13 or -14 => type.Value == 31,
        -26 or -27 => type.Value == 3,
        -28 or -29 => type.Value == 132,
        -30 or -31 => type.Value == 186,
        -32 or -33 => type.Value == 187,
        -34 or -35 => type.Value == 188,
        -36 or -37 => type.Value == 189,
        -44 or -45 => type.Value == 200,
        -46 or -47 => type.Value == 21,
        -48 or -49 => type.Value == 201,
        -50 or -51 => type.Value == 202,
        -52 or -53 => type.Value == 203,
        -54 or -55 => type.Value == 223,
        201 or 202 or 203 or 635 => type.Value == netId.Value,
        _ => false
    };

    // Lookup is used by live geometry and dispatch callers. Only initialization walks the captured
    // metadata; immutable host-classic definitions are materialized once, without selecting any AI.
    private static readonly VanillaNpcDefinition?[] Sources = CreateSources();
    private static readonly VanillaNpcDefinition?[] Definitions = CreateDefinitions();

    public static bool IsSupported(NpcTypeId type) =>
        type.Value is >= 1 and <= 696 && Sources[type.Value + 65].HasValue;

    public static bool TryGet(NpcTypeId type, NpcNetId netId, out VanillaNpcDefinition definition) =>
        TryGet(Definitions, type, netId, out definition);

    internal static bool TryGetSource(NpcTypeId type, NpcNetId netId, out VanillaNpcDefinition definition) =>
        TryGet(Sources, type, netId, out definition);

    private static bool TryGet(VanillaNpcDefinition?[] rows, NpcTypeId type, NpcNetId netId,
        out VanillaNpcDefinition definition)
    {
        if (netId.Value is >= -65 and <= 696 && rows[netId.Value + 65] is VanillaNpcDefinition found &&
            found.Type == type)
        {
            definition = found;
            return true;
        }
        definition = default;
        return false;
    }

    private static VanillaNpcDefinition?[] CreateSources()
    {
        var rows = new VanillaNpcDefinition?[762];
        for (int type = 1; type <= 696; type++)
        {
            if (!CreateSource(new(type), new((short)type), out var source)) continue;
            rows[type + 65] = source;
            for (short net = -65; net < 0; net++)
                if (CreateSource(new(type), new(net), out var variant)) rows[net + 65] = variant;
        }
        return rows;
    }

    private static VanillaNpcDefinition?[] CreateDefinitions()
    {
        var rows = new VanillaNpcDefinition?[Sources.Length];
        // The original Windows x86 CLR retains wider products through integer truncation. Materialize
        // its classic body/stat results too, instead of exposing the Linux capture on every host.
        var context = new VanillaNpcSpawnContext(1f, 1, false);
        for (int index = 0; index < Sources.Length; index++)
        {
            if (Sources[index] is not VanillaNpcDefinition definition) continue;
            if (!VanillaNpcSpawnDefaults.TryResolveGroundRoster(in definition, in context,
                    OperatingSystem.IsWindows(), out var classic)) continue;
            rows[index] = definition with
            {
                SourceHitboxAtSpawn = classic.Hitbox,
                LifeMax = classic.LifeMax,
                Damage = classic.Damage,
                Defense = classic.Defense,
                KnockBackResist = classic.KnockBackResist ?? definition.KnockBackResist
            };
        }
        return rows;
    }

    private static bool CreateSource(NpcTypeId type, NpcNetId netId, out VanillaNpcDefinition definition)
    {
        if (!VanillaNpcSourceDefaultRoster1458.TryGet(type, netId, out var source) ||
            source.AiStyle.Value is not (3 or 7))
        {
            definition = default;
            return false;
        }

        // Raw dimensions are separately verified by SetDefaults(sizeScaleOverride:1), not inferred
        // by dividing the rounded post-scale body. No member of this roster has raw height16/32.
        var raw = RawBody(type, source);
        definition = source with
        {
            DefinitionOnly = false,
            CreationDefaultsOnly = true,
            BaseWidth = raw.Width,
            BaseHeight = raw.Height,
            SourceNetId = netId.Value < 0 ? netId : default,
            Role = type.Value is 37 or 368 || VanillaTownNpcFacts1458.IsHousingEligible(type)
                ? NpcArchetypeRole.Town : NpcArchetypeRole.Ordinary
        };
        return true;
    }

    internal static float BaselineMoney(NpcTypeId type) => type.Value switch
    {
        17 or 18 or 19 or 20 or 22 or 37 or 38 or 46 or 54 or 107 or 108 or 124 or 142 or 148 or 149 or 160 or 178 or 207 or 208 or 209 or 227 or 228 or 229 or 230 or 299 or 300 or 303 or 337 or 353 or 361 or 362 or 364 or 366 or 367 or 368 or 369 or 409 or 411 or 415 or 419 or 424 or 425 or 426 or 427 or 428 or 429 or 441 or 443 or 445 or 447 or 453 or 518 or 520 or 534 or 538 or 539 or 540 or 550 or 588 or 593 or 602 or 608 or 610 or 614 or 616 or 617 or 624 or 625 or 633 or 637 or 638 or 639 or 640 or 641 or 642 or 643 or 644 or 645 or 646 or 647 or 648 or 649 or 650 or 651 or 652 or 656 or 663 or 670 or 678 or 679 or 680 or 681 or 682 or 683 or 684 or 687 => 0f,
        582 => 40f,
        187 or 433 => 55f,
        3 or 67 or 217 or 218 or 219 or 220 or 331 or 332 or 430 or 590 or 591 or 632 => 60f,
        132 or 186 or 200 or 432 or 436 => 65f,
        189 or 223 or 435 => 70f,
        161 or 188 or 431 or 434 or 580 or 691 => 80f,
        319 => 85f,
        21 or 26 or 164 or 449 or 635 => 100f,
        320 => 105f,
        203 or 452 => 110f,
        202 or 321 or 323 or 451 or 494 or 495 or 498 or 499 or 500 or 501 or 502 or 503 or 504 or 505 or 506 => 120f,
        31 or 201 or 239 or 294 or 295 or 296 or 322 or 324 or 450 => 130f,
        28 or 489 => 150f,
        508 => 175f,
        27 or 73 or 111 or 167 or 181 or 185 => 200f,
        44 => 250f,
        481 => 300f,
        77 or 110 or 140 => 400f,
        47 or 163 or 168 or 197 or 206 or 464 or 470 or 482 or 524 => 500f,
        78 or 120 or 162 or 385 or 462 or 528 or 529 or 530 => 600f,
        198 or 199 or 525 or 526 => 650f,
        79 or 80 or 212 or 630 => 700f,
        527 => 750f,
        532 => 800f,
        350 => 900f,
        52 or 53 or 104 or 166 or 213 or 214 or 236 or 254 or 269 or 270 or 271 or 272 or 273 or 274 or 275 or 276 or 277 or 278 or 279 or 280 or 291 or 292 or 293 or 379 or 380 or 381 or 382 or 460 or 461 or 480 or 536 or 586 => 1000f,
        255 or 305 or 306 or 307 or 308 or 309 or 310 or 311 or 312 or 313 or 314 or 338 or 339 or 340 or 383 or 386 or 389 or 391 => 1200f,
        257 or 468 or 469 => 1300f,
        215 or 258 or 348 or 349 or 466 => 1500f,
        342 => 1800f,
        287 or 326 => 2000f,
        343 or 351 or 463 => 3000f,
        159 or 251 or 471 => 5000f,
        109 => 10000f,
        243 => 15000f,
        196 => 20000f,
        631 => 25000f,
        216 or 290 => 50000f,
        _ => 0f
    };

    private static VanillaNpcHitboxSize RawBody(NpcTypeId type, VanillaNpcDefinition source) => type.Value switch
    {
        26 or 27 or 28 or 111 => new(18, 38),
        73 or 306 or 307 or 308 or 309 or 311 or 312 or 313 or 314 or
            319 or 320 or 321 or 324 or 339 or 340 => new(18, 40),
        631 => new(36, 48),
        // Signed identities use their positive type's raw body, including type77's height override.
        3 or 21 or 31 or 77 or 132 or 186 or 187 or 188 or 189 or 200 or 201 or 202 or 203 or 223
            => new(18, 40),
        _ => source.SourceHitboxAtSpawn ?? default
    };
}
