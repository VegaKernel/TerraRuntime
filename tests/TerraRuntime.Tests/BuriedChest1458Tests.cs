using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for TerrariaServer 1.4.5.8 <c>WorldGen.AddBuriedChest</c>.
/// </summary>
/// <remarks>
/// <para>
/// Expectations come from calling the unmodified method inside the pinned dedicated server on the same
/// synthetic ground, and checking everything it produces: whether each call took a chest, where each one
/// landed, the next four shared RNG values, a SHA-256 over every field of every cell, and every chest's
/// ordered inventory with stacks and prefixes. The inventory is the decisive column - the tile hash only
/// proves a two-by-two chest of the right style appeared, while the loot is where the other hundred and
/// eighty draws went.
/// </para>
/// <para>
/// The ground is a stack of floors twelve rows apart in seven material bands, so the same call lands on dirt,
/// stone, snow, ice, mud, sand or marble depending only on the column, which is what the frozen branch keys
/// on. The fixtures then walk the four depth bands and the thirteen identities that override them, including
/// the ones that only appear with an explicit signature item: a dungeon chest with its shadow key, a
/// living-wood chest inside a living tree, an ivy chest with the living-mahogany wands, a desert container
/// behind a hive wall, the Lihzahrd chest with its power cell, and a water chest.
/// </para>
/// <para>
/// Several fixtures call more than once on purpose. The shadow key, the ram rune and the living-mahogany
/// wands are guaranteed the first time and rationed afterwards, and the underworld walks a six-item cycle, so
/// a port that treated each chest as independent would pass the first call of every fixture and fail the rest.
/// The eight-call underworld fixture is what pins that cycle.
/// </para>
/// <para>
/// Four fixtures refuse, and each refuses for a different reason: a chest already standing beside the site,
/// a column with no ground at all, a boulder inside the five-square box around the sample, and lava in the
/// footprint. All four refuse before taking a single value, which is what proves the refusals are not paid
/// for. A fifth - grass and cobwebs filling the footprint - does NOT refuse, because the placement cuts them.
/// </para>
/// </remarks>
public sealed class BuriedChest1458Tests
{
    private const int Width = 1400;
    private const int Height = 900;
    private const double WorldSurface = 220.0;
    private const double RockLayer = 320.0;

    [Theory]
    // fixture, seed, taken/x/y per call, four next draws, worldHash, inventories
    [InlineData("surface", 42, "1,199,39", 147761, 906212, 692589, 516413,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]3069x1 282x58 22x4 965x75 28x3 2350x3 290x1 8x17 5508x1")]
    [InlineData("surface", 1458, "1,199,39", 378917, 592272, 287113, 471335,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]953x1p78 22x6 40x39 2350x5")]
    [InlineData("under", 42, "1,199,255", 539970, 44147, 711154, 147761,
        "f24f041a4f28cc3b3244d6759dcea6bc233a2973708ae4e22214fd0b4f9415c9",
        "[199,254]5011x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10")]
    [InlineData("under", 1458, "1,199,255", 859310, 542152, 548956, 401207,
        "f24f041a4f28cc3b3244d6759dcea6bc233a2973708ae4e22214fd0b4f9415c9",
        "[199,254]53x1p78 52x1 21x9 2350x3")]
    [InlineData("cavern", 42, "1,199,399", 147761, 906212, 692589, 516413,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("cavern", 1458, "1,199,399", 859310, 542152, 548956, 401207,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]53x1p70 167x1 21x6")]
    [InlineData("deep", 42, "1,199,663", 577196, 397470, 152193, 90112,
        "e85a515924c7f05e0a7bb2c834da66f2a8e3d5b0c92986fa850151ce01faedef",
        "[199,662]53x1 167x1 265x68 301x1 8x18 5484x1")]
    [InlineData("deep", 1458, "1,199,663", 581833, 745894, 954365, 145804,
        "e85a515924c7f05e0a7bb2c834da66f2a8e3d5b0c92986fa850151ce01faedef",
        "[199,662]50x1 227x19 302x2 5506x1")]
    [InlineData("hell", 42, "1,199,699", 147761, 906212, 692589, 516413,
        "19f0c61ea08e910793ca293cd9bfd7e54048c02584e2a70ef8ce533d8806d5e6",
        "[199,698]220x1p37 5010x1p65 167x1 265x62 227x17 294x1 300x1 4870x1 5508x1")]
    [InlineData("hell", 1458, "1,199,699", 378917, 592272, 287113, 471335,
        "19f0c61ea08e910793ca293cd9bfd7e54048c02584e2a70ef8ce533d8806d5e6",
        "[199,698]220x1 19x21 300x2 282x23")]
    [InlineData("surfacemain", 42, "1,199,39", 147761, 906212, 692589, 516413,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]3069x1 282x58 22x4 965x75 28x3 2350x3 290x1 8x17 5508x1")]
    [InlineData("surfacemain", 1458, "1,199,39", 378917, 592272, 287113, 471335,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]953x1p78 22x6 40x39 2350x5")]
    [InlineData("primary", 42, "1,199,399", 577196, 397470, 152193, 90112,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]285x1p64 43x1 51x31 188x5 8x19 5484x1")]
    [InlineData("primary", 1458, "1,199,399", 145804, 859310, 542152, 548956,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]285x1p73 279x34 303x2")]
    [InlineData("ice", 42, "1,699,399", 539970, 44147, 711154, 147761,
        "7a35bb050a5021181f641730031688a922d5dbe468be96a4a4ac8e36d0f80fbe",
        "[699,398]987x1 5120x1 167x1 279x31 188x4 2350x3 282x24 73x1 2198x1")]
    [InlineData("ice", 1458, "1,699,399", 548956, 401207, 378917, 592272,
        "7a35bb050a5021181f641730031688a922d5dbe468be96a4a4ac8e36d0f80fbe",
        "[699,398]950x1p76 167x1 21x6")]
    [InlineData("gold", 42, "1,199,399", 147761, 906212, 692589, 516413,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("gold", 1458, "1,199,399", 859310, 542152, 548956, 401207,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]53x1p70 167x1 21x6")]
    [InlineData("sky", 42, "1,199,39", 147761, 906212, 692589, 516413,
        "4c734e8ee2b663b277369276b55bb42776b2cc007f3068dfee682c8a8a859d95",
        "[199,38]5011x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10 5238x1 751x52")]
    [InlineData("sky", 1458, "1,199,39", 401207, 378917, 592272, 287113,
        "4c734e8ee2b663b277369276b55bb42776b2cc007f3068dfee682c8a8a859d95",
        "[199,38]53x1p78 52x1 21x9 2350x3 2197x1 5388x1 751x77")]
    [InlineData("water", 42, "1,899,399", 147761, 906212, 692589, 516413,
        "f5ccfd7de6fbf17c0f34880f0079efa1d0887a8df39ec05276e7f6276162e663",
        "[899,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("water", 1458, "1,899,399", 859310, 542152, 548956, 401207,
        "f5ccfd7de6fbf17c0f34880f0079efa1d0887a8df39ec05276e7f6276162e663",
        "[899,398]53x1p70 167x1 21x6")]
    [InlineData("near", 42, "0,0,0", 668106, 140907, 125518, 522764,
        "8631bfc2a95ef3ae75c62608beca5f337364b5f2650a154de9ff3512619c83cc",
        "none")]
    [InlineData("near", 1458, "0,0,0", 422351, 621669, 892512, 757953,
        "8631bfc2a95ef3ae75c62608beca5f337364b5f2650a154de9ff3512619c83cc",
        "none")]
    [InlineData("noground", 42, "0,0,0", 668106, 140907, 125518, 522764,
        "3a1553704a5471d07e0cc38321ce8b468e7adc45c1e528ce2ae94aee22547508",
        "none")]
    [InlineData("noground", 1458, "0,0,0", 422351, 621669, 892512, 757953,
        "3a1553704a5471d07e0cc38321ce8b468e7adc45c1e528ce2ae94aee22547508",
        "none")]
    [InlineData("dungeon", 42, "1,199,255 1,319,255 1,439,255 1,559,255", 162362, 103142, 452151, 333248,
        "908bf49105245a433602afa466a41989aac55301708295d4f88008bd25fa7780",
        "[199,254]155x1p6 329x1 5465x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10 [319,254]155x1 965x52 21x13 42x45 298x1 2192x1 [439,254]155x1p12 6156x1p71 166x14 22x8 40x42 28x4 2350x3 72x50 5499x1 [559,254]155x1 5465x1p79 965x69 22x12 28x3 291x2")]
    [InlineData("dungeon", 1458, "1,199,255 1,319,255 1,439,255 1,559,255", 729329, 250087, 840599, 843296,
        "908bf49105245a433602afa466a41989aac55301708295d4f88008bd25fa7780",
        "[199,254]155x1 329x1 5465x1p70 166x18 21x14 42x39 298x2 2350x3 8x14 [319,254]155x1 329x1 [439,254]155x1 21x6 28x4 8x11 2192x1 [559,254]155x1 329x1 965x52 40x44 298x1 2350x3 72x52")]
    [InlineData("dungeondeep", 42, "1,199,399 1,319,399 1,439,399 1,559,399", 452151, 333248, 776110, 148549,
        "dec081eb26102fa48ddbac174e8a425a36293304c4e87ddd5245035a4c7b7361",
        "[199,398]155x1p6 329x1 5465x1 43x1 167x1 279x31 188x4 2350x3 282x24 73x1 2192x1 [319,398]155x1p39 51x26 21x9 279x45 296x1 73x1 [439,398]155x1p6 329x1 43x1 41x34 188x3 296x1 282x22 73x2 2192x1 5499x1 [559,398]155x1p2 51x34 19x9 188x3 302x2 73x2")]
    [InlineData("dungeondeep", 1458, "1,199,399 1,319,399 1,439,399 1,559,399", 729329, 250087, 840599, 843296,
        "dec081eb26102fa48ddbac174e8a425a36293304c4e87ddd5245035a4c7b7361",
        "[199,398]155x1 329x1 5465x1p70 43x1 279x49 188x4 303x2 [319,398]155x1p12 5465x1p67 [439,398]155x1p46 21x4 188x4 2350x2 73x1 [559,398]155x1 167x1 19x9 2350x3 73x1")]
    [InlineData("lockedbiome", 42, "1,199,255 1,319,255", 748358, 384573, 149119, 26339,
        "4a359a5e2ed62ea54d3110c0438b62956e67294101926fa3b324bdbfa707f3b4",
        "[199,254]155x1p6 166x15 52x1 965x86 42x30 28x4 2350x2 72x82 5234x1 [319,254]155x1 21x6 298x1 2192x1")]
    [InlineData("lockedbiome", 1458, "1,199,255 1,319,255", 617153, 767792, 379865, 298083,
        "4a359a5e2ed62ea54d3110c0438b62956e67294101926fa3b324bdbfa707f3b4",
        "[199,254]155x1 21x8 28x5 289x2 2350x4 72x84 [319,254]155x1p56 52x1 965x65 21x10 304x1 5234x1")]
    [InlineData("livingwood", 42, "1,199,255 1,319,255 1,439,255", 12033, 797809, 439179, 513496,
        "8f2fb5283621c35612382077a6f9eb2454670c441af10bb3861986dacaf2abc2",
        "[199,254]5629x1 4429x1 5528x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10 [319,254]5629x1 4427x1 22x8 2350x4 72x55 5499x1 [439,254]5629x1 166x17 22x11 28x4 291x1 8x12 72x78 5505x1")]
    [InlineData("livingwood", 1458, "1,199,255 1,319,255 1,439,255", 185128, 230551, 96666, 632673,
        "8f2fb5283621c35612382077a6f9eb2454670c441af10bb3861986dacaf2abc2",
        "[199,254]5629x1 5528x1 21x8 28x5 289x2 2350x4 72x84 [319,254]5629x1 5528x1 22x7 42x39 2329x2 8x18 [439,254]5629x1 5629x1 5528x1 965x79 2350x2 72x70")]
    [InlineData("livingsurf", 42, "1,199,39 1,319,39", 515913, 50794, 420366, 706582,
        "acb98017985f7907599335d83d327294c2c32c24f50338aee432002bf220e702",
        "[199,38]5629x1 4429x1 5528x1 168x5 20x7 965x69 290x1 8x17 5508x1 [319,38]5629x1 5629x1 5528x1 3093x1 168x4 40x25 28x5 2350x3 72x22 9x52")]
    [InlineData("livingsurf", 1458, "1,199,39 1,319,39", 924257, 799589, 839988, 232306,
        "acb98017985f7907599335d83d327294c2c32c24f50338aee432002bf220e702",
        "[199,38]5629x1 5528x1 22x6 40x39 2350x5 [319,38]5629x1 279x190 42x44 28x3 2350x5 298x1 9x64")]
    [InlineData("barewood", 42, "1,199,255", 539970, 44147, 711154, 147761,
        "52501c4e4c48b4b4fa41700b276f6b43d9394ae9a22041bbee87e7f79867a127",
        "[199,254]5011x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10")]
    [InlineData("barewood", 1458, "1,199,255", 859310, 542152, 548956, 401207,
        "52501c4e4c48b4b4fa41700b276f6b43d9394ae9a22041bbee87e7f79867a127",
        "[199,254]53x1p78 52x1 21x9 2350x3")]
    [InlineData("ivy", 42, "1,199,399 1,319,399 1,439,399", 34539, 913514, 928970, 461193,
        "5818c631e911aea0a77f65dccad2c4b2e19f6d841f7c8f56879076a4b13c4c4e",
        "[199,398]211x1p64 3360x1 3361x1 43x1 167x1 279x31 188x4 2350x3 282x24 73x1 2204x1 [319,398]211x1p62 51x26 21x9 279x45 296x1 73x1 [439,398]211x1 3360x1 3361x1 43x1 41x34 188x3 296x1 282x22 73x2 2204x1 753x1 5508x1")]
    [InlineData("ivy", 1458, "1,199,399 1,319,399 1,439,399", 739613, 925940, 331460, 53492,
        "5818c631e911aea0a77f65dccad2c4b2e19f6d841f7c8f56879076a4b13c4c4e",
        "[199,398]211x1p73 3360x1 3361x1 51x48 21x6 2204x1 [319,398]211x1p69 167x1 19x7 305x2 2350x4 [439,398]211x1p69 51x38 21x4 188x3 8x30 73x1")]
    [InlineData("ivyshallow", 42, "1,199,255 1,319,255", 289606, 648145, 143646, 44498,
        "acce2bd7a522eb143ad1ea895f283c2999e83ece97f43083e63c9f18218f22d7",
        "[199,254]5011x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10 5508x1 [319,254]49x1p75 166x13 21x13 28x3 2204x1")]
    [InlineData("ivyshallow", 1458, "1,199,255 1,319,255", 617153, 767792, 379865, 298083,
        "acce2bd7a522eb143ad1ea895f283c2999e83ece97f43083e63c9f18218f22d7",
        "[199,254]53x1p78 52x1 21x9 2350x3 2204x1 [319,254]54x1p69 52x1 965x65 21x10 304x1")]
    [InlineData("lihzahrd", 42, "1,199,399", 147761, 906212, 692589, 516413,
        "f28403b1316f209474e1fff56ef2d2e6534b49f2cda88c534970094d7653d62c",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 2195x1 2767x1")]
    [InlineData("lihzahrd", 1458, "1,199,399", 542152, 548956, 401207, 378917,
        "f28403b1316f209474e1fff56ef2d2e6534b49f2cda88c534970094d7653d62c",
        "[199,398]53x1p70 167x1 21x6 2195x1 2767x1")]
    [InlineData("style32", 42, "1,199,399 1,319,399 1,439,399", 34539, 913514, 928970, 461193,
        "10473b99ea886877b1911a47a3bb7570a1c017116f9832207173677f8f64293e",
        "[199,398]5011x1p38 4450x1 51x44 19x7 41x31 2329x1 2350x2 [319,398]49x1p75 4779x1 4780x1 4781x1 43x1 167x1 188x4 303x1 2350x2 [439,398]724x1p57 3199x1 5120x1 19x8 188x3 295x1 301x1 2350x4 282x22 73x2 2198x1 669x1 5508x1")]
    [InlineData("style32", 1458, "1,199,399 1,319,399 1,439,399", 256563, 591339, 818151, 739613,
        "10473b99ea886877b1911a47a3bb7570a1c017116f9832207173677f8f64293e",
        "[199,398]53x1p70 4779x1 4780x1 4781x1 43x1 279x49 188x4 303x2 [319,398]53x1p63 4450x1 303x1 [439,398]724x1p36 279x28 2350x2 974x25 73x1")]
    [InlineData("waterstyle", 42, "1,199,255 1,319,255 1,439,255", 12033, 797809, 439179, 513496,
        "28cc22915a55aaa5c086fa7bc0f90f6d52e5e6ef55fe3c22a7199a371a7784c5",
        "[199,254]670x1p37 4425x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10 [319,254]670x1p36 4460x1 22x8 2350x4 72x55 5499x1 [439,254]670x1p56 4425x1 166x16 52x1 965x80 40x42 8x12 72x78 5505x1")]
    [InlineData("waterstyle", 1458, "1,199,255 1,319,255 1,439,255", 185128, 230551, 96666, 632673,
        "28cc22915a55aaa5c086fa7bc0f90f6d52e5e6ef55fe3c22a7199a371a7784c5",
        "[199,254]670x1 4460x1 52x1 21x9 2350x3 [319,254]670x1 4425x1 4460x1 22x7 42x39 2329x2 8x18 [439,254]670x1p53 2350x2 72x70")]
    [InlineData("desert", 42, "1,399,399 1,519,399 1,639,399", 628463, 218445, 13253, 365161,
        "b4429fc4e4c1c916e3e094f260bdb7c49568fb67479650a27d9d2f72ea5b7895",
        "[399,398]4262x1 4346x1 167x1 188x5 8x19 5484x1 [519,398]4262x1 43x1 279x28 302x1 2350x3 73x2 [639,398]4262x1 43x1 167x1 41x45 188x4 73x1")]
    [InlineData("desert", 1458, "1,399,399 1,519,399 1,639,399", 155060, 394790, 256563, 591339,
        "b4429fc4e4c1c916e3e094f260bdb7c49568fb67479650a27d9d2f72ea5b7895",
        "[399,398]4055x1p78 167x1 21x6 [519,398]4263x1 19x3 41x36 302x2 2351x1 2350x4 [639,398]4055x1p76 279x28 2350x2 8x25 73x1")]
    [InlineData("ocean", 42, "1,199,399", 147761, 906212, 692589, 516413,
        "5e8338e875ecca918ecf5546cc497efd849cf00e0ae4a28da98cef20c56ed6d9",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("ocean", 1458, "1,199,399", 859310, 542152, 548956, 401207,
        "5e8338e875ecca918ecf5546cc497efd849cf00e0ae4a28da98cef20c56ed6d9",
        "[199,398]53x1p70 167x1 21x6")]
    [InlineData("slope", 42, "1,199,399", 147761, 906212, 692589, 516413,
        "6b25c74dea08941f7b5d196bcdbb87a39ceda0686964d5ed6a7812cbca4f1197",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("slope", 1458, "1,199,399", 859310, 542152, 548956, 401207,
        "6b25c74dea08941f7b5d196bcdbb87a39ceda0686964d5ed6a7812cbca4f1197",
        "[199,398]53x1p70 167x1 21x6")]
    [InlineData("boulder", 42, "0,0,0", 668106, 140907, 125518, 522764,
        "1c9dcaa726374c26c4a7cf4adca605c6154b311b906ba056eee31133624ecd4c",
        "none")]
    [InlineData("boulder", 1458, "0,0,0", 422351, 621669, 892512, 757953,
        "1c9dcaa726374c26c4a7cf4adca605c6154b311b906ba056eee31133624ecd4c",
        "none")]
    [InlineData("shimmer", 42, "0,0,0", 668106, 140907, 125518, 522764,
        "2d6d2b4d47ca1d1c23ddd41a91e2bb1337aa263a1345c091c031289f785c0df5",
        "none")]
    [InlineData("shimmer", 1458, "0,0,0", 422351, 621669, 892512, 757953,
        "2d6d2b4d47ca1d1c23ddd41a91e2bb1337aa263a1345c091c031289f785c0df5",
        "none")]
    [InlineData("plants", 42, "1,199,399", 147761, 906212, 692589, 516413,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("plants", 1458, "1,199,399", 859310, 542152, 548956, 401207,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]53x1p70 167x1 21x6")]
    [InlineData("lava", 42, "0,0,0", 668106, 140907, 125518, 522764,
        "d476572764f95bf29a3c30e3fd75bfafd703dea1494088b581afd233912f9c09",
        "none")]
    [InlineData("lava", 1458, "0,0,0", 422351, 621669, 892512, 757953,
        "d476572764f95bf29a3c30e3fd75bfafd703dea1494088b581afd233912f9c09",
        "none")]
    [InlineData("hellcycle", 42, "1,199,699 1,319,699 1,439,699 1,559,699 1,679,699 1,799,699 1,919,699 1,1039,699", 81839, 420511, 805139, 880092,
        "44de4bd4b685eb7b6e37b668d61310e579c50b4dc0f48477fcbf0fcd9be0bdd6",
        "[199,698]220x1p37 5010x1p65 167x1 265x62 227x17 294x1 300x1 4870x1 5508x1 [319,698]218x1 167x1 19x27 278x70 295x1 73x2 [439,698]112x1p43 5010x1 4551x1 227x15 293x1 305x1 2350x2 73x2 [559,698]96x1 5010x1 4737x1 117x24 265x53 227x19 288x1 300x2 4870x2 282x17 73x3 [679,698]65x1p6 5010x1 227x18 297x1 2345x1 2350x1 282x21 73x4 2197x1 5258x1 751x66 [799,698]5011x1p59 5010x1 278x65 2350x1 [919,698]220x1 278x59 293x2 300x2 8x15 73x4 [1039,698]218x1p57 117x23 297x1 300x2 4870x2 8x18")]
    [InlineData("hellcycle", 1458, "1,199,699 1,319,699 1,439,699 1,559,699 1,679,699 1,799,699 1,919,699 1,1039,699", 367955, 696956, 274864, 862830,
        "44de4bd4b685eb7b6e37b668d61310e579c50b4dc0f48477fcbf0fcd9be0bdd6",
        "[199,698]220x1 19x21 300x2 282x23 [319,698]218x1p57 4737x1 167x1 19x23 304x1 [439,698]112x1p42 227x18 301x2 2350x1 282x15 73x4 [559,698]96x1 167x1 265x69 295x1 288x2 2350x1 [679,698]65x1 227x20 73x3 5258x1 751x88 [799,698]5011x1p36 19x20 296x2 300x2 2350x1 73x3 [919,698]220x1 4443x1 19x22 278x67 294x1 301x2 2350x2 282x24 [1039,698]218x1 4551x1 167x1 117x22 227x16 300x1 2350x2 8x25")]
    public void Chest_matches_official(
        string fixture, int seed, string placements, int d0, int d1, int d2, int d3,
        string worldHash, string inventory)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);
        var context = new BuriedChestContext1458
        {
            Height = Height,
            WorldSurface = WorldSurface,
            RockLayer = RockLayer,
            LavaLine = Height - 300,
            CopperBar = 20,
            IronBar = 22,
            SilverBar = 21,
            GoldBar = 19,
            TungstenIsSilverTier = false,
            DesertHiveLow = 500,
            DesertHiveHigh = 600,
            HellChestItem = [220, 218, 112, 96, 65, 5011]
        };

        var placer = new BuriedChest1458(store, random, context);
        Call call = Fixture(fixture);
        var actualPlacements = new StringBuilder();
        for (int index = 0; index < call.Calls; index++)
        {
            bool placed = placer.TryAdd(call.X + index * call.Step, call.Y, out int x, out int y,
                call.Primary, call.NotNear, call.Style, call.TrySlope, call.TileType);
            actualPlacements.Append(placed ? 1 : 0).Append(',').Append(x).Append(',').Append(y).Append(' ');
        }

        string expected = $"{placements}|{d0}|{d1}|{d2}|{d3}|{worldHash}|{inventory}";
        string actual = $"{actualPlacements.ToString().TrimEnd()}|" +
            $"{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|" +
            $"{Hash(store)}|{Inventory(placer)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    private readonly record struct Call(
        int X, int Y, int Primary, bool NotNear, int Style, bool TrySlope, ushort TileType, int Calls, int Step);

    private static Call Fixture(string fixture) => fixture switch
    {
        "surface" => new Call(200, 40, 0, false, -1, false, 0, 1, 0),
        "under" => new Call(200, 250, 0, false, -1, false, 0, 1, 0),
        "cavern" => new Call(200, 400, 0, false, -1, false, 0, 1, 0),
        "deep" => new Call(200, 660, 0, false, -1, false, 0, 1, 0),
        "hell" => new Call(200, 700, 0, false, -1, false, 0, 1, 0),
        "surfacemain" => new Call(200, 40, 0, false, 0, false, 0, 1, 0),
        "primary" => new Call(200, 400, 285, false, -1, false, 0, 1, 0),
        "ice" => new Call(700, 400, 0, false, 11, false, 0, 1, 0),
        "gold" => new Call(200, 400, 0, false, 1, false, 0, 1, 0),
        "sky" => new Call(200, 40, 0, false, 13, false, 0, 1, 0),
        "water" => new Call(900, 400, 0, false, 4, false, 467, 1, 0),
        "near" => new Call(200, 400, 0, true, -1, false, 0, 1, 0),
        "noground" => new Call(1200, 400, 0, false, -1, false, 0, 1, 0),
        "dungeon" => new Call(200, 250, 155, false, 2, false, 0, 4, 120),
        "dungeondeep" => new Call(200, 400, 155, false, 2, false, 0, 4, 120),
        "lockedbiome" => new Call(200, 250, 155, false, 23, false, 0, 2, 120),
        "livingwood" => new Call(200, 250, 5629, false, 12, false, 0, 3, 120),
        "livingsurf" => new Call(200, 40, 5629, false, 12, false, 0, 2, 120),
        "barewood" => new Call(200, 250, 0, false, 12, false, 0, 1, 0),
        "ivy" => new Call(200, 400, 211, false, 10, false, 0, 3, 120),
        "ivyshallow" => new Call(200, 250, 0, false, 10, false, 0, 2, 120),
        "lihzahrd" => new Call(200, 400, 0, false, 16, false, 0, 1, 0),
        "style32" => new Call(200, 400, 0, false, 32, false, 0, 3, 120),
        "waterstyle" => new Call(200, 250, 670, false, 17, false, 0, 3, 120),
        "desert" => new Call(400, 400, 0, false, -1, false, 0, 3, 120),
        "ocean" => new Call(200, 400, 0, false, 17, true, 0, 1, 0),
        "slope" => new Call(200, 400, 0, false, -1, true, 0, 1, 0),
        "boulder" => new Call(200, 400, 0, false, -1, false, 0, 1, 0),
        "shimmer" => new Call(200, 396, 0, false, -1, false, 0, 1, 0),
        "plants" => new Call(200, 400, 0, false, -1, false, 0, 1, 0),
        "lava" => new Call(200, 400, 0, false, -1, false, 0, 1, 0),
        _ => new Call(200, 700, 0, false, -1, false, 0, 8, 120)
    };

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, new WorldTile { FrameX = -1, FrameY = -1 });

        // Floors twelve rows apart so the descent always finds ground quickly, in seven material bands:
        // dirt, stone, snow, ice, mud, sand, marble. Column 1150 onward is hollow so one fixture falls
        // through the whole world.
        ushort[] floors = [0, 1, 147, 161, 59, 53, 367];
        for (int y = 40; y < Height - 6; y += 12)
        for (int x = 0; x < 1150; x++)
            store.Set(x, y, new WorldTile
            {
                Flags = WorldTileFlags.Active, Type = floors[(x / 170) % floors.Length],
                FrameX = -1, FrameY = -1
            });

        // A wall everywhere behind the floors. The dungeon fixtures carry theirs on the floor cells too,
        // because the dungeon test reads the wall of the solid tile the descent landed on.
        ushort wall = fixture switch
        {
            "dungeon" or "dungeondeep" or "lockedbiome" => 7,
            "livingwood" or "livingsurf" => 244,
            "desert" => 187,
            _ => 2
        };
        bool wallEverywhere = fixture is "dungeon" or "dungeondeep" or "lockedbiome";
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (!wallEverywhere && tile.IsActive)
                continue;
            tile.Wall = wall;
            store.Set(x, y, in tile);
        }

        if (fixture == "near")
        {
            for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                WorldTile tile = store.Get(210 + column, 398 + row);
                tile.Flags |= WorldTileFlags.Active;
                tile.Type = 21;
                tile.FrameX = (short)(column * 18);
                tile.FrameY = (short)(row * 18);
                store.Set(210 + column, 398 + row, in tile);
            }
        }

        if (fixture == "boulder")
        {
            WorldTile tile = store.Get(201, 399);
            tile.Flags |= WorldTileFlags.Active;
            tile.Type = 138;
            store.Set(201, 399, in tile);
        }

        if (fixture == "shimmer")
        {
            WorldTile tile = store.Get(200, 396);
            tile.LiquidAmount = 255;
            tile.LiquidKind = WorldLiquidKind.Shimmer;
            store.Set(200, 396, in tile);
        }

        if (fixture == "plants")
        {
            for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                WorldTile tile = store.Get(199 + column, 398 + row);
                tile.Flags |= WorldTileFlags.Active;
                tile.Type = (ushort)(row == 0 ? 51 : 3);
                store.Set(199 + column, 398 + row, in tile);
            }
        }

        if (fixture == "lava")
        {
            for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                WorldTile tile = store.Get(199 + column, 398 + row);
                tile.LiquidAmount = 255;
                tile.LiquidKind = WorldLiquidKind.Lava;
                store.Set(199 + column, 398 + row, in tile);
            }
        }

        // Sloped ground under the sample, which only a trySlope caller flattens. Shape two is vanilla
        // slope one.
        if (fixture is "ocean" or "slope")
        {
            for (int x = 150; x < 300; x++)
            {
                WorldTile tile = store.Get(x, 400);
                tile.Shape = 2;
                store.Set(x, 400, in tile);
            }
        }

        return store;
    }

    private static string Inventory(BuriedChest1458 placer)
    {
        var sb = new StringBuilder();
        foreach (BuriedChestResult1458 chest in placer.Chests)
        {
            sb.Append('[').Append(chest.Left).Append(',').Append(chest.Top).Append(']');
            foreach (WorldGenerationChestItem item in chest.Items)
            {
                if (item.ItemType.Value == 0 || item.Stack == 0)
                    continue;
                sb.Append(item.ItemType.Value).Append('x').Append(item.Stack);
                if (item.Prefix.Value != 0)
                    sb.Append('p').Append(item.Prefix.Value);
                sb.Append(' ');
            }
        }

        return sb.Length == 0 ? "none" : sb.ToString().TrimEnd();
    }

    private static string Hash(WorldTileStore store)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            sb.Append(tile.IsActive ? '1' : '0').Append(',')
              .Append(tile.Type).Append(',')
              .Append(tile.Wall).Append(',')
              .Append(tile.FrameX).Append(',')
              .Append(tile.FrameY).Append(',')
              .Append(tile.LiquidAmount).Append(',')
              .Append((int)tile.LiquidKind).Append(',')
              .Append(slope).Append(',')
              .Append(tile.Shape == 1 ? '1' : '0').Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
