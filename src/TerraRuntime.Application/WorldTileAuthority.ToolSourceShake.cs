using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private enum ShakeOutcome : byte
        {
            Honey, Bomb, LivingWoodWand, LeafWand, LivingMahoganyWand, RichMahoganyLeafWand,
            ForestRareOne, ForestRareTwo, GeneralRare, Acorn, MushroomSeed, HalloweenItem,
            Wood, Coins, Bird, Fairy, Owl, Dragonfly, Raven, Birds, Bees, Seagull, Crimson,
            Corrupt, JungleBat, JungleHive, Butterfly, AshCritter, RemixAshItem,
            ForestFruit, SnowFruit, JungleFruit, PalmFruit, CorruptFruit, HallowFruit, CrimsonFruit, AshFruit
        }
        private static readonly (int Chance, ShakeOutcome Outcome)[] ShakeBranches =
        [
            (50, ShakeOutcome.Honey), (17, ShakeOutcome.Bomb),
            (300, ShakeOutcome.LivingWoodWand), (300, ShakeOutcome.LeafWand),
            (200, ShakeOutcome.LivingMahoganyWand), (200, ShakeOutcome.RichMahoganyLeafWand),
            (1000, ShakeOutcome.ForestRareOne), (1000, ShakeOutcome.ForestRareTwo),
            (100, ShakeOutcome.GeneralRare), (7, ShakeOutcome.Acorn), (8, ShakeOutcome.MushroomSeed),
            (35, ShakeOutcome.HalloweenItem), (12, ShakeOutcome.Wood), (20, ShakeOutcome.Coins),
            (15, ShakeOutcome.Bird), (50, ShakeOutcome.Fairy), (50, ShakeOutcome.Owl),
            (50, ShakeOutcome.Dragonfly), (40, ShakeOutcome.Raven), (50, ShakeOutcome.Birds),
            (40, ShakeOutcome.Bees), (20, ShakeOutcome.Seagull), (30, ShakeOutcome.Crimson),
            (30, ShakeOutcome.Corrupt), (30, ShakeOutcome.JungleBat), (40, ShakeOutcome.JungleHive),
            (20, ShakeOutcome.Butterfly), (20, ShakeOutcome.AshCritter), (20, ShakeOutcome.RemixAshItem),
            (12, ShakeOutcome.ForestFruit), (12, ShakeOutcome.SnowFruit), (12, ShakeOutcome.JungleFruit),
            (12, ShakeOutcome.PalmFruit), (12, ShakeOutcome.CorruptFruit), (12, ShakeOutcome.HallowFruit),
            (12, ShakeOutcome.CrimsonFruit), (12, ShakeOutcome.AshFruit)
        ];

        private bool TryShakeTree(int hitX, int hitY, in WorldTile hit)
        {
            if (server is null) return false;
            if (!server.CanAttemptSourceTreeShake) return true;
            var geometry = new ToolTreeGeometry1458(Overlay.Tiles, Overlay);
            if (!geometry.TryResolveBottom(hitX, hitY, out var bottom)) return false;
            if (bottom.Biome == ToolTreeBiome1458.None) return true;
            if (!server.TryReserveSourceTreeShake(bottom.X, bottom.Y, out bool fresh)) return false;
            if (!fresh) return true;
            int topY = bottom.Y - 1;
            while (topY > 10)
            {
                if (!Overlay.TryRead(bottom.X, topY, out var tile) ||
                    !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var tileFacts)) return false;
                if (!tile.IsActive || !tileFacts.IsShakeable) break;
                topY--;
            }
            topY++;
            if (!Overlay.TryRead(bottom.X, topY, out var top)) return false;
            bool leafy = top.IsActive && IsFramedTree(top.Type) &&
                (top.Type == 323 && top.FrameX >= 88 || top.FrameX == 22 && top.FrameY is >= 198 and <= 242);
            if (!leafy) return true;
            if (bottom.X < 2 || bottom.X + 2 >= Overlay.Tiles.Dimensions.WidthTiles ||
                topY < 2 || topY + 2 >= Overlay.Tiles.Dimensions.HeightTiles - 40) return true;
            for (int x = bottom.X - 2; x <= bottom.X + 2; x++)
                for (int y = topY - 2; y <= topY + 2; y++)
                {
                    if (!Overlay.TryRead(x, y, out var tile) ||
                        !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var tileFacts)) return false;
                    if (tile.IsActive && !tile.IsActuated && tileFacts.IsSolid && !tileFacts.IsSolidTop) return true;
                }
            foreach (var branch in ShakeBranches)
            {
                // These three flags precede the roll in source. Every remaining
                // biome/time predicate is evaluated AFTER its draw, even when false.
                if (branch.Outcome == ShakeOutcome.Honey &&
                    !(facts.TenthAnniversaryWorld && facts.NotTheBeesWorld && !facts.DrunkWorld)) continue;
                if (branch.Outcome == ShakeOutcome.Bomb && !facts.GoodWorld) continue;
                if (branch.Outcome == ShakeOutcome.RemixAshItem && !facts.RemixWorld) continue;
                int chance = branch.Outcome == ShakeOutcome.Bomb && facts.TenthAnniversaryWorld && !facts.RemixWorld
                    ? branch.Chance * 2 : branch.Chance;
                if (random.Next(chance) != 0 || !AllowsShakeBranch(branch.Outcome, bottom.Biome, bottom.X, topY)) continue;
                if (!TryExecuteShake(branch.Outcome, hitX, hitY, bottom.X, topY, in hit)) return false;
                break;
            }
            if (!Overlay.TryRead(bottom.X, bottom.Y, out var ground) ||
                !TryTreeLeafStyle(bottom.X, in top, in ground, out short style)) return false;
            return style < 0 || server.TryStageSourceTreeEffect(bottom.X, topY, style);
        }

        private bool AllowsShakeBranch(ShakeOutcome branch, ToolTreeBiome1458 biome, int x, int y)
        {
            bool forest = biome == ToolTreeBiome1458.Forest, hallow = biome == ToolTreeBiome1458.Hallowed;
            bool jungle = biome == ToolTreeBiome1458.Jungle;
            bool palm = biome is ToolTreeBiome1458.Palm or ToolTreeBiome1458.PalmCorrupt or ToolTreeBiome1458.PalmCrimson or ToolTreeBiome1458.PalmHallowed;
            bool corrupt = biome is ToolTreeBiome1458.Corrupt or ToolTreeBiome1458.PalmCorrupt;
            bool crimson = biome is ToolTreeBiome1458.Crimson or ToolTreeBiome1458.PalmCrimson;
            return branch switch
            {
                ShakeOutcome.LivingWoodWand or ShakeOutcome.LeafWand or ShakeOutcome.ForestRareOne or
                    ShakeOutcome.ForestRareTwo or ShakeOutcome.ForestFruit => forest,
                ShakeOutcome.LivingMahoganyWand or ShakeOutcome.RichMahoganyLeafWand or ShakeOutcome.Bees or
                    ShakeOutcome.JungleHive or ShakeOutcome.JungleFruit => jungle,
                ShakeOutcome.GeneralRare or ShakeOutcome.Acorn => forest || hallow || biome is ToolTreeBiome1458.Snow or ToolTreeBiome1458.Ash,
                ShakeOutcome.MushroomSeed => biome == ToolTreeBiome1458.Mushroom,
                ShakeOutcome.HalloweenItem => facts.Halloween,
                ShakeOutcome.Bird or ShakeOutcome.Birds => forest || hallow,
                ShakeOutcome.Fairy => hallow && !facts.DayTime,
                ShakeOutcome.Owl => forest && !facts.DayTime,
                ShakeOutcome.Dragonfly => jungle && facts.DayTime,
                ShakeOutcome.Raven => forest && !facts.DayTime && facts.Halloween,
                ShakeOutcome.Seagull or ShakeOutcome.PalmFruit => palm && !facts.IsPalmOasis(x),
                ShakeOutcome.Crimson or ShakeOutcome.CrimsonFruit => crimson,
                ShakeOutcome.Corrupt or ShakeOutcome.CorruptFruit => corrupt,
                ShakeOutcome.JungleBat => jungle && !facts.DayTime,
                ShakeOutcome.Butterfly => (forest || hallow) && !facts.Raining && !facts.TooWindyForButterflies && facts.DayTime,
                ShakeOutcome.AshCritter or ShakeOutcome.RemixAshItem => biome == ToolTreeBiome1458.Ash && y > Overlay.Tiles.Dimensions.HeightTiles - 250,
                ShakeOutcome.SnowFruit => biome == ToolTreeBiome1458.Snow,
                ShakeOutcome.HallowFruit => hallow || biome == ToolTreeBiome1458.PalmHallowed,
                ShakeOutcome.AshFruit => biome == ToolTreeBiome1458.Ash,
                _ => true
            };
        }

        private bool TryExecuteShake(ShakeOutcome branch, int hitX, int hitY, int x, int y, in WorldTile hit)
        {
            bool Item(int type, int count = 1) => server!.TryStageSourceItem(new(type), count, x, y);
            bool Npc(int type, float vy = 0) => TryTreeNpc(type, x * 16, y * 16, 0, vy, default, false);
            switch (branch)
            {
                case ShakeOutcome.Honey: return server!.TryStageSourceItem(new(1130), random.Next(12, 21), hitX, hitY);
                case ShakeOutcome.Bomb:
                    return TryTreeProjectile(facts.TenthAnniversaryWorld && !facts.RemixWorld ? 75 : 28,
                        x * 16, y * 16, random.Next(-100, 101) * .002f, 0, 0, new(16, 16, 0), true);
                case ShakeOutcome.LivingWoodWand: return Item(832);
                case ShakeOutcome.LeafWand: return Item(933);
                case ShakeOutcome.LivingMahoganyWand: return Item(3360);
                case ShakeOutcome.RichMahoganyLeafWand: return Item(3361);
                case ShakeOutcome.ForestRareOne: return Item(5517);
                case ShakeOutcome.ForestRareTwo: return Item(4366);
                case ShakeOutcome.GeneralRare: return Item(5629);
                case ShakeOutcome.Acorn: return Item(27, random.Next(1, 3));
                case ShakeOutcome.MushroomSeed: return Item(194, random.Next(1, 2));
                case ShakeOutcome.HalloweenItem: return Item(1809, random.Next(1, 3));
                case ShakeOutcome.Wood:
                    if (!TryResolveTreeDrops(hitX, hitY, in hit, out var offers)) return false;
                    return offers.Primary.IsNone || Item(offers.Primary.Value, random.Next(1, 4));
                case ShakeOutcome.Coins:
                    int coin = 71, count = random.Next(50, 100);
                    if (random.Next(30) == 0)
                    { coin = 73; count = 1; if (random.Next(5) == 0) count++; if (random.Next(10) == 0) count++; }
                    else if (random.Next(10) == 0)
                    {
                        coin = 72; count = random.Next(1, 21);
                        if (random.Next(3) == 0) count += random.Next(1, 21);
                        if (random.Next(4) == 0) count += random.Next(1, 21);
                    }
                    return Item(coin, count);
                case ShakeOutcome.Bird:
                    ReadOnlySpan<int> birds = [74, 297, 298, 299, 538];
                    int bird = birds[random.Next(5)];
                    if (!TryClosestGoldRoll(x, y, out int roll)) return false;
                    if (roll == 0) bird = random.Next(2) != 0 ? 539 : 442;
                    return Npc(bird);
                case ShakeOutcome.Fairy:
                    int fairy = 583 + random.Next(3);
                    if (facts.TenthAnniversaryWorld && random.Next(4) != 0) fairy = 583;
                    return Npc(fairy);
                case ShakeOutcome.Owl: return Npc(611, 1);
                case ShakeOutcome.Dragonfly: return Npc(671 + random.Next(5), 1);
                case ShakeOutcome.Raven: return Npc(301);
                case ShakeOutcome.Birds:
                    for (int index = 0; index < 5; index++)
                    {
                        int bx = x + random.Next(-2, 2), by = y - 1 + random.Next(-2, 2);
                        if (!TryClosestGoldRoll(x, y, out int goldRoll)) return false;
                        ReadOnlySpan<int> flock = [74, 297, 298];
                        int npc = goldRoll != 0 ? flock[random.Next(3)] : 442;
                        if (!TryTreeNpc(npc, bx * 16, by * 16, 0, 0, default, false,
                            circularVelocity: true)) return false;
                    }
                    return true;
                case ShakeOutcome.Bees:
                    for (int index = 0; index < 5; index++)
                    {
                        int bx = x + random.Next(-2, 2), by = y - 1 + random.Next(-2, 2);
                        if (!TryTreeNpc(210 + random.Next(2), bx * 16, by * 16, 0, 0,
                            new(0, 65, 0, 0), true)) return false;
                    }
                    return true;
                case ShakeOutcome.Seagull: return Npc(603);
                case ShakeOutcome.Crimson: return TryTreeNpc(-22, x * 16 + 8, (y - 1) * 16, 0, 0, default, false);
                case ShakeOutcome.Corrupt: return TryTreeNpc(-11, x * 16 + 8, (y - 1) * 16, 0, 0, default, false);
                case ShakeOutcome.JungleBat: return Npc(51);
                case ShakeOutcome.JungleHive: return TryTreeProjectile(655, x * 16 + 8, (y - 1) * 16, 0, 0, 0, default, false);
                case ShakeOutcome.Butterfly:
                    if (!TryClosestGoldRoll(x, y, out int butterflyRoll)) return false;
                    return Npc(butterflyRoll == 0 ? 444 : 356);
                case ShakeOutcome.AshCritter:
                    ReadOnlySpan<int> ash = [654, 653, 655]; return Npc(ash[random.Next(3)]);
                case ShakeOutcome.RemixAshItem: return Item(965, random.Next(20, 41));
                case ShakeOutcome.ForestFruit:
                    ReadOnlySpan<int> fruit = [4009, 4293, 4282, 4290, 4291]; return Item(fruit[random.Next(5)]);
                case ShakeOutcome.SnowFruit: return Item(random.Next(2) != 0 ? 4295 : 4286);
                case ShakeOutcome.JungleFruit: return Item(random.Next(2) != 0 ? 4292 : 4294);
                case ShakeOutcome.PalmFruit: return Item(random.Next(2) != 0 ? 4287 : 4283);
                case ShakeOutcome.CorruptFruit: return Item(random.Next(2) != 0 ? 4289 : 4284);
                case ShakeOutcome.HallowFruit: return Item(random.Next(2) != 0 ? 4288 : 4297);
                case ShakeOutcome.CrimsonFruit: return Item(random.Next(2) != 0 ? 4285 : 4296);
                case ShakeOutcome.AshFruit: return Item(random.Next(2) != 0 ? 5278 : 5277);
                default: return false;
            }
        }

        private bool TryResolveTreeDrops(int x, int y, in WorldTile tile, out ToolTreeDrops1458 drops)
        {
            drops = default;
            if (server is null) return false;
            var geometry = new ToolTreeGeometry1458(Overlay.Tiles, Overlay);
            if (!geometry.TryResolveBottom(x, y, out var bottom) ||
                !VanillaToolTreeActors1458.TrySelect(actors, bottom.X * 16, bottom.Y * 16, 16, 16, out var closest)) return false;
            ushort palmGround = 0;
            if (tile.Type == 323 && !geometry.TryResolvePalmDropGround(x, y, out palmGround)) return false;
            var context = new ToolTreeDropContext1458(bottom.Biome, palmGround, closest.SelectedAxe,
                facts.TenthAnniversaryWorld, facts.DrunkWorld, facts.RemixWorld, facts.NotTheBeesWorld);
            return VanillaToolTreeDrops1458.TryResolve(in tile, in context, server.Random, out drops);
        }

        private bool TryClosestGoldRoll(int x, int y, out int roll)
        {
            roll = 0;
            if (!VanillaToolTreeActors1458.TrySelect(actors, x * 16, y * 16, 1, 1, out var closest)) return false;
            const int GoldCritterChance = 400;
            float luck = closest.Luck;
            if (luck > 0f && (float)random.NextDouble() < luck)
                roll = random.Next(random.Next(GoldCritterChance / 2, GoldCritterChance));
            else if (luck < 0f && (float)random.NextDouble() < -luck)
                roll = random.Next(random.Next(GoldCritterChance, GoldCritterChance * 2));
            else roll = random.Next(GoldCritterChance);
            return true;
        }

        private bool TryTreeNpc(int sourceType, int bottomX, int bottomY, float vx, float vy,
            NpcAiState afterAi, bool replaceable, bool circularVelocity = false)
        {
            if (server is null) return false;
            int canonical = sourceType == -22 ? 173 : sourceType == -11 ? 6 : sourceType;
            var intent = new NpcAiSpawnIntent(new(canonical), bottomX, bottomY, 0, 0, byte.MaxValue)
            { NetIdOverride = new(sourceType) };
            if (!server.TryStageSourceNpc(in intent, out bool allocated, out var birth)) return false;
            if (circularVelocity)
            {
                float angle = ((float)Math.PI * 2f) * (float)random.NextDouble();
                vx = (float)Math.Cos(angle) * 3f; vy = (float)Math.Sin(angle) * 3f;
            }
            if (!allocated) return true; // Source overflow-object writes are not an active birth.
            if (!VanillaNpcDefinitionCatalog.TryGet(new(birth.Type), new(birth.NetId), out var definition) ||
                !definition.TryResolveHitbox(birth.Simulation, out var body)) return false;
            bool wet = false;
            if (!definition.NoTileCollideAtSpawn && !TryNpcWet(birth.PositionX, birth.PositionY, body.Width, body.Height, out wet)) return false;
            return server.TryFinishSourceNpc(in birth, vx, vy, afterAi, replaceable, wet, out _);
        }

        private bool TryTreeProjectile(int type, float centerX, float centerY, float vx, float vy,
            short damage, ProjectileAiState ai, bool npcProjectile)
        {
            if (server?.SourceProjectiles is not { } store ||
                !VanillaDefinitionCatalog.TryGet(new(type), out var definition)) return false;
            var state = new ProjectileStateUpdate(new(type), byte.MaxValue,
                centerX - definition.Width * .5f, centerY - definition.Height * .5f,
                vx, vy, ai, 0, damage, 0, 0);
            return server.TryStageSourceProjectile(store, in state, npcProjectile, out _);
        }

        private bool TryNpcWet(float x, float y, int width, int height, out bool wet)
        {
            wet = false;
            var dimensions = Overlay.Tiles.Dimensions;
            int pw = Math.Min(10, width), ph = height / 2;
            float px = x + width / 2 - pw / 2, py = y + height / 2 - ph / 2;
            int left = Math.Clamp((int)(x / 16f) - 1, 0, dimensions.WidthTiles - 1);
            int right = Math.Clamp((int)((x + width) / 16f) + 2, 0, dimensions.WidthTiles - 1);
            int top = Math.Clamp((int)(y / 16f) - 1, 0, Math.Max(0, dimensions.HeightTiles - 40));
            int bottom = Math.Clamp((int)((y + height) / 16f) + 2, 0, Math.Max(0, dimensions.HeightTiles - 40));
            for (int tx = left; tx < right; tx++) for (int ty = top; ty < bottom; ty++)
            {
                if (!Overlay.TryRead(tx, ty, out var tile)) return false;
                float liquidY = ty * 16; int liquidHeight;
                if (tile.LiquidAmount > 0)
                { float empty = (256 - tile.LiquidAmount) / 32f * 2f; liquidY += empty; liquidHeight = 16 - (int)empty; }
                else
                {
                    if (!tile.IsActive || tile.SourceSlope == 0 || ty == 0) continue;
                    if (!Overlay.TryRead(tx, ty - 1, out var above)) return false;
                    if (above.LiquidAmount == 0) continue;
                    liquidHeight = 16;
                }
                if (px + pw > tx * 16 && px < tx * 16 + 16 && py + ph > liquidY && py < liquidY + liquidHeight)
                { wet = true; return true; }
            }
            return true;
        }

        private bool TryTreeLeafStyle(int x, in WorldTile top, in WorldTile ground, out short style)
        {
            style = top.Type switch
            { 596 => 1248, 616 => 1257, 634 => 1278, >= 583 and <= 589 => (short)(1249 + top.Type - 583), _ => -1 };
            if (style >= 0) return true;
            style = ground.Type switch
            { 2 or 477 => 910, 60 => 914, 70 => 912, 23 or 112 => 915, 199 or 234 => 916, 53 => 911, 116 => 919, 147 => 913, 633 => 1278, _ => -1 };
            if (ground.Type is not (109 or 492)) return true;
            if (facts.HallowBackground is not { } background) return false;
            int variant = top.FrameX is 22 or 44 or 66 ? top.FrameY == 220 ? 1 : top.FrameY == 242 ? 2 : 0 : 0;
            if (background is 2 or 3)
            {
                ReadOnlySpan<int> values = [0, 0, 0, 1, 2, 1, 3, 4, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8];
                style = (short)(917 + 196 + values[variant + x % 6 * 3]);
            }
            else
            {
                ReadOnlySpan<int> values = [2, 1, 7, 4, 5, 6, 3, 8, 0];
                style = (short)(917 + values[variant + x % 3 * 3]);
            }
            return true;
        }
    }
}
