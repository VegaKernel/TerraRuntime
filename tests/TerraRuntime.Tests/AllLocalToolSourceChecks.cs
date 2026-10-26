using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Tiles;
using Xunit;

namespace TerraRuntime.Tests.Tools1458;

internal static partial class AllLocalToolSourceChecks
{
    private static string context = "initial";

    internal static void Run()
    {
        var failures = new List<string>();
        foreach (string platform in new[] { "Linux", "Windows" })
            foreach (string repeat in new[] { "First", "Repeat" })
            {
                bool windows = platform == "Windows";
                using var metadata = Read("Metadata", platform, repeat);
                using var extra = Read("ExtraItems", platform, repeat);
                var toolFacts = metadata.RootElement.EnumerateArray()
                    .Where(row => IsTool(row.GetProperty("facts")))
                    .ToDictionary(row => I(row, "input"), row => row.GetProperty("facts"));
                var extraFacts = extra.RootElement.EnumerateArray()
                    .ToDictionary(row => I(row, "item"), row => row.GetProperty("facts"));
                Section("catalog", () => CheckCatalog(metadata.RootElement, toolFacts, extraFacts, windows));
                Section("prefixes", () => CheckPrefixes("Prefixes", toolFacts, extraFacts, platform, repeat, windows));
                Section("prefixes85", () => CheckPrefixes("Prefixes85", toolFacts, extraFacts, platform, repeat, windows));
                Section("variants", () => CheckVariants(platform, repeat, extraFacts, windows));
                Section("tile roles", () => CheckTileRoles(platform, repeat));
                Section("hit cache", () => CheckHits(platform, repeat));
                Section("final pick/shovel", () => CheckFinalPowerAndShovel(platform, repeat));
                Section("pick power", () => CheckPower(platform, repeat));
                Section("action arithmetic", () => CheckActionClock(platform, repeat, windows));
                Section("clock stages", () => CheckClocks(platform, repeat, toolFacts, windows));
                Section("held AI", () => CheckHeld(platform, repeat, toolFacts, windows));
                Section("birth/axe/health", () => CheckExtension(platform, repeat, windows));
                Section("definitions/direction", () => CheckDefinitions(platform, repeat));
                Section("dust", () => CheckDust(platform, repeat));

                void Section(string name, Action check)
                {
                    context = name;
                    try { check(); }
                    catch (Exception error)
                    {
                        failures.Add($"{platform}/{repeat}/{name}/{context}: {error}");
                    }
                }
            }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool IsTool(JsonElement facts) => I(facts, "type") > 0 &&
        (I(facts, "pick") > 0 || I(facts, "axe") > 0 || I(facts, "hammer") > 0 || I(facts, "type") == 4711);

    private static void CheckCatalog(JsonElement rows, Dictionary<int, JsonElement> facts,
        Dictionary<int, JsonElement> extra, bool windows)
    {
        Assert.Equal(6196, rows.GetArrayLength());
        Assert.Equal(106, facts.Count);
        Assert.Equal(106, VanillaToolCatalog1458.Count);
        foreach (var row in rows.EnumerateArray())
        {
            int id = I(row, "input");
            context = id.ToString();
            bool expected = facts.ContainsKey(id);
            Assert.Equal(expected, VanillaToolCatalog1458.TryGet(new(id), out var tool));
            if (!expected) continue;
            CheckTool(row.GetProperty("facts"), tool);
            Assert.Equal(I(extra[id], "crit"), tool.Crit);
            Assert.Equal(B(extra[id], "NoMeleeSpeedBonus"), tool.NoMeleeSpeedBonus);
            Assert.Equal(0, I(extra[id], "mana"));
            Assert.Equal(0, I(extra[id], "useAmmo"));
            Assert.False(B(extra[id], "consumable"));
            Assert.True(VanillaSelectedItemCrit1458.TryResolve(new(id), default,
                windows ? VanillaBulletSourceArithmetic1458.WindowsClr4X86 : VanillaBulletSourceArithmetic1458.CoreClrSingle,
                false, out int crit));
            Assert.Equal(I(extra[id], "crit"), crit);
        }
    }

    private static void CheckTool(JsonElement facts, ToolDefinition1458 actual)
    {
        Assert.Equal(I(facts, "type"), actual.Item.Value);
        Assert.Equal(I(facts, "prefix"), actual.Prefix.Value);
        Assert.Equal(I(facts, "pick"), actual.Pick);
        Assert.Equal(I(facts, "axe"), actual.Axe);
        Assert.Equal(I(facts, "hammer"), actual.Hammer);
        Assert.Equal(I(facts, "useTime"), actual.UseTime);
        Assert.Equal(I(facts, "useAnimation"), actual.UseAnimation);
        Assert.Equal(I(facts, "useStyle"), actual.UseStyle);
        Assert.Equal(I(facts, "tileBoost"), actual.TileBoost);
        Assert.Equal(B(facts, "channel"), actual.Channel);
        Assert.Equal(B(facts, "autoReuse"), actual.AutoReuse);
        Assert.Equal(B(facts, "useTurn"), actual.UseTurn);
        Assert.Equal(B(facts, "melee"), actual.Melee);
        Assert.Equal(B(facts, "noMelee"), actual.NoMelee);
        Assert.Equal(B(facts, "noUseGraphic"), actual.NoUseGraphic);
        Assert.Equal(I(facts, "shoot"), actual.Shoot);
        Assert.Equal(I(facts, "damage"), actual.Damage);
        Assert.Equal(B(facts, "IsDrill"), actual.IsDrill);
        Assert.Equal(B(facts, "IsChainsaw"), actual.IsChainsaw);
        var bits = facts.GetProperty("singleBits");
        Assert.Equal(I(bits, "shootSpeed"), BitConverter.SingleToInt32Bits(actual.ShootSpeed));
        Assert.Equal(I(bits, "knockBack"), BitConverter.SingleToInt32Bits(actual.KnockBack));
        Assert.Equal(I(bits, "scale"), BitConverter.SingleToInt32Bits(actual.Scale));
    }

    private static void CheckPrefixes(string category, Dictionary<int, JsonElement> facts,
        Dictionary<int, JsonElement> extra, string platform, string repeat, bool windows)
    {
        using var document = Read(category, platform, repeat);
        Assert.Equal(category == "Prefixes" ? 9010 : 1378, document.RootElement.GetArrayLength());
        foreach (var row in document.RootElement.EnumerateArray())
        {
            int id = I(row, "id");
            var expected = row.GetProperty("after");
            int retained = I(expected, "prefix");
            context = $"{id}/{I(row, "inputPrefix")}->{retained}";
            // Prefix(requested) may reroll. This API resolves the retained tuple,
            // and does not claim requested-prefix acceptance or client RNG custody.
            Assert.True(VanillaToolCatalog1458.TryResolve(new(id), new(retained), false, windows, out var tool));
            CheckTool(expected, tool);
            Assert.True(VanillaItemPrefixTable1458.TryGetStatModifiers(new(retained), out var existingPrefix));
            Assert.Equal(I(extra[id], "crit") + existingPrefix.CritBonus, tool.Crit);
            Assert.True(VanillaSelectedItemCrit1458.TryResolve(new(id), new(retained),
                windows ? VanillaBulletSourceArithmetic1458.WindowsClr4X86 : VanillaBulletSourceArithmetic1458.CoreClrSingle,
                false, out int crit));
            Assert.Equal(tool.Crit, crit);
            Assert.Equal(I(facts[id], "axe"), tool.Axe);
        }
    }

    private static void CheckVariants(string platform, string repeat,
        Dictionary<int, JsonElement> extra, bool windows)
    {
        using var document = Read("Variants", platform, repeat);
        Assert.Equal(848, document.RootElement.GetArrayLength());
        foreach (var row in document.RootElement.EnumerateArray())
        {
            int id = I(row, "id"), mask = I(row, "worldMask");
            context = $"{id}/world{mask}";
            Assert.True(VanillaToolCatalog1458.TryResolve(new(id), default, (mask & 1) != 0,
                windows, out var tool));
            CheckTool(row.GetProperty("facts"), tool);
            Assert.Equal(I(extra[id], "crit"), tool.Crit);
        }
    }

    private static void CheckTileRoles(string platform, string repeat)
    {
        using var direct = Read("TileArrays", platform, repeat);
        using var conversion = Read("Conversion", platform, repeat);
        using var jungle = Read("JungleGrass", platform, repeat);
        string[] keys = ["Main.tileAxe", "Main.tileHammer", "Main.tileNoFail", "TileID.Sets.IsATreeTrunk",
            "TileID.Sets.PreventsTileRemovalIfOnTopOfIt", "TileID.Sets.BasicChest", "TileID.Sets.BasicChestFake",
            "TileID.Sets.BasicDresser", "TileID.Sets.Boulders", "TileID.Sets.Platforms", "Main.tileMoss",
            "TileID.Sets.tileMossBrick", "Main.tileAlch", "TileID.Sets.IsVine", "TileID.Sets.TruncatesWalls",
            "Main.tileSolid", "Main.tileSolidTop", "Main.tileDungeon", "Main.tileRope", "TileID.Sets.Clouds",
            "TileID.Sets.CanBeDugByShovel", "TileID.Sets.IsLivingFire", "TileID.Sets.IsShakeable"];
        Assert.Equal(754, VanillaToolTileCatalog1458.TileCount);
        for (int id = 0; id < 754; id++)
        {
            context = id.ToString();
            Assert.True(VanillaToolTileCatalog1458.TryGet(new(id), out var tile));
            bool[] actual = [tile.IsAxeTarget, tile.IsHammerTarget, tile.NoFail, tile.IsTreeTrunk,
                tile.PreventsTileRemovalAbove, tile.IsBasicChest, tile.IsFakeBasicChest, tile.IsBasicDresser,
                tile.IsBoulder, tile.IsPlatform, tile.IsMoss, tile.IsMossBrick, tile.IsAlchemyPlant,
                tile.IsVine, tile.TruncatesWalls, tile.IsSolid, tile.IsSolidTop, tile.IsDungeon, tile.IsRope,
                tile.IsCloud, tile.CanBeDugByShovel, tile.IsLivingFire, tile.IsShakeable];
            for (int i = 0; i < keys.Length; i++)
                Assert.Equal(direct.RootElement.GetProperty(keys[i])[id].GetBoolean(), actual[i]);
            Assert.Equal(conversion.RootElement.GetProperty("Stone")[id].GetBoolean(), tile.ConvertsAsStone);
            Assert.Equal(conversion.RootElement.GetProperty("Moss")[id].GetBoolean(), tile.ConvertsAsMoss);
            Assert.Equal(conversion.RootElement.GetProperty("Grass")[id].GetBoolean(), tile.ConvertsAsGrass);
            Assert.Equal(jungle.RootElement[id].GetBoolean(), tile.ConvertsAsJungleGrass);
            Assert.Equal(id != 504, VanillaToolDamage1458.CanPickTarget(new(id)));
        }
        Assert.False(VanillaToolTileCatalog1458.TryGet(new(754), out _));
    }

    private static ToolHitState1458 Hit(JsonElement source)
    {
        var entries = source.GetProperty("entries").EnumerateArray().Select(entry =>
            new ToolHitEntry1458(entry[0].GetInt32(), entry[1].GetInt32(), entry[2].GetInt32(),
                entry[3].GetInt32(), entry[4].GetInt32())).ToArray();
        int[] order = Ints(source.GetProperty("order"));
        Assert.True(ToolHitState1458.TryCreateSnapshot(entries, order, I(source, "buffer"), out var state));
        return state!;
    }

    private static void EqualHit(JsonElement expected, ToolHitState1458 actual)
    {
        Assert.Equal(I(expected, "buffer"), actual.BufferLocation);
        for (int i = 0; i < ToolHitState1458.SlotCount; i++)
        {
            var entry = expected.GetProperty("entries")[i];
            Assert.Equal(new ToolHitEntry1458(entry[0].GetInt32(), entry[1].GetInt32(),
                entry[2].GetInt32(), entry[3].GetInt32(), entry[4].GetInt32()), actual.GetEntry(i));
            Assert.Equal(expected.GetProperty("order")[i].GetInt32(), actual.GetOrder(i));
        }
    }

    private static void CheckHits(string platform, string repeat)
    {
        using var document = Read("Hit", platform, repeat);
        Assert.Equal(231, document.RootElement.GetArrayLength());
        bool[] present = Enumerable.Repeat(true, 501).ToArray();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var before = Hit(row.GetProperty("before"));
            var random = Random(row.GetProperty("beforeRandom"));
            string op = row.GetProperty("op").GetString()!;
            int[] args = Ints(row.GetProperty("args"));
            context = $"{I(row, "seed")}/{op}/{string.Join(',', args)}";
            ToolHitState1458 after;
            switch (op)
            {
                case "hit":
                    Assert.True(before.TryHitObject(args[0], args[1], args[2], out after, out int slot));
                    Assert.Equal(row.GetProperty("result").GetInt32(), slot);
                    break;
                case "add":
                    Assert.True(before.TryAddDamage(args[0], args[1], args[2] != 0,
                        out after, out int damage, out int samples));
                    Assert.Equal(row.GetProperty("result").GetInt32(), damage);
                    for (int i = 0; i < samples; i++) random.Next();
                    break;
                case "clear": after = before.Clear(args[0]); break;
                case "move": after = before.UpdatePosition(args[0], args[1], args[2]); break;
                case "prune": Assert.True(before.TryPrune(present, out after)); break;
                case "at": Assert.True(before.TryClearThisTile(args[0], args[1], present, out after)); break;
                default: throw new InvalidDataException($"Unknown original cache operation {op}");
            }
            EqualHit(row.GetProperty("after"), after);
            AssertRandom(row.GetProperty("afterRandom"), random);
        }
    }

    private static void CheckPower(string platform, string repeat)
    {
        using var contextDocument = Read("PowerContext", platform, repeat);
        using var document = Read("Power", platform, repeat);
        using var frames = Read("PowerFrames", platform, repeat);
        var contexts = contextDocument.RootElement.GetProperty("contexts").EnumerateArray()
            .ToDictionary(value => value.GetProperty("label").GetString()!, value => value);
        Assert.Equal(131196, document.RootElement.GetArrayLength());
        foreach (var row in document.RootElement.EnumerateArray())
        {
            string label = row.GetProperty("context").GetString()!;
            var c = contexts[label];
            int type = I(row, "type"), power = I(row, "power");
            context = $"{label}/{type}/{power}";
            var window = new ToolTargetTile1458[25];
            window[12] = new(new(type), 0, 0, false, 0);
            var world = new ToolDamageWorld1458(100, 400, 80d, 200, B(c, "good"),
                B(c, "infected"), B(c, "bees"), B(c, "noTraps"), B(c, "anniversary"));
            Assert.True(VanillaToolDamage1458.TryGetPickDamage(power, I(c, "x"), I(c, "y"), window,
                world, out var result));
            EqualPower(row, result);
        }
        Assert.Equal(576, frames.RootElement.GetArrayLength());
        foreach (var row in frames.RootElement.EnumerateArray())
        {
            int type = I(row, "type");
            context = $"frame/{type}/{I(row, "frameX")}/{I(row, "frameY")}/{I(row, "power")}";
            var window = Enumerable.Repeat(new ToolTargetTile1458(new(type), 0, 18, false, 0), 25).ToArray();
            window[12] = new(new(type), (short)I(row, "frameX"), (short)I(row, "frameY"), false, 0);
            Assert.True(VanillaToolDamage1458.TryGetPickDamage(I(row, "power"), 50, 50, window,
                new(100, 400, 80d, 200, false, false, false, false, false), out var result));
            EqualPower(row, result);
        }
    }

    private static void EqualPower(JsonElement expected, ToolPickDamage1458 result)
    {
        Assert.Equal(I(expected, "damage"), result.Damage);
        Assert.Equal(I(expected, "hitX"), result.HitX);
        Assert.Equal(I(expected, "hitY"), result.HitY);
        Assert.Equal(B(expected, "blockMouse"), result.BlockMouse);
    }

    private static JsonDocument Read(string category, string platform, string repeat)
    {
        string name = $"AllTools{category}{platform}{repeat}1458";
        using var source = typeof(AllLocalTools1458Tests).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing source fixture {name}");
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static int I(JsonElement e, string name) => e.GetProperty(name).GetInt32();
    private static bool B(JsonElement e, string name) => e.GetProperty(name).GetBoolean();
    private static int[] Ints(JsonElement e) => e.EnumerateArray().Select(value => value.GetInt32()).ToArray();
    private static float F(JsonElement e, string name) => e.GetProperty(name).ValueKind == JsonValueKind.String
        ? float.Parse(e.GetProperty(name).GetString()!, System.Globalization.CultureInfo.InvariantCulture)
        : e.GetProperty(name).GetSingle();
    private static float Bit(JsonElement e, string name) =>
        BitConverter.Int32BitsToSingle(e.GetProperty("bits").GetProperty(name).GetInt32());
    private static float V(JsonElement e, string vector, int axis) =>
        BitConverter.Int32BitsToSingle(e.GetProperty(vector).GetProperty("bits")[axis].GetInt32());
    private static void EqualFloat(float expected, float actual) =>
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

    private static VanillaUnifiedRandom1458 Random(JsonElement source)
    {
        var random = new VanillaUnifiedRandom1458(0);
        Cursor(random) = source.GetProperty("cursor").GetUInt32();
        RandomState(random) = Ints(source.GetProperty("state"));
        return random;
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 actual)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), Cursor(actual));
        Assert.Equal(Ints(expected.GetProperty("state")), RandomState(actual));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] RandomState(VanillaUnifiedRandom1458 random);
}
