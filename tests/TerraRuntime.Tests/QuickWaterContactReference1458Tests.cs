using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class QuickWaterContactReference1458Tests
{
    // Official1.4.5.8 QuickWater: four liquid kinds, a stone cave, waterLine180.
    // Full normalized16-byte cells including inactive liquid-kind bits; x-major.
    [Theory]
    [InlineData(42, false, "38B39DE12352E72F49F34CDF42E5CEE4DD4DCC28CF2F96BF54CA7452D5AFB351")]
    [InlineData(1458, false, "B9099BB6428268A5A23E1A37D9DFA591B9B62D9D76FF9D146BFCB5ACA6727B86")]
    [InlineData(8675309, false, "809A63B3826F4027CEA321E54E3DC9E447D55B49BA759820656852C47E3C5B37")]
    [InlineData(42, true, "21B2981104EEAEF30E93241B3193AE9E1AD1754FAB1404749DFCC2AE7BBB6F14")]
    [InlineData(1458, true, "BD139C60F1E3848C90742B085514AFBD21664202F3060018F8FEF64C838A07D8")]
    [InlineData(8675309, true, "5EB0A56E0B6EFD7591655DFBA6D05BC30A17D4A69430778339CF29409344E2AB")]
    [InlineData(42, false, "2823A7B60D0180F320336657C966EB3B1570A2C2B18F90AA7D0A7CBA65C04603", 187, false)]
    [InlineData(1458, false, "B82332D23784379BCD5B9F899EECF079742B56549F49729254CF707060FEEFEB", 187, false)]
    [InlineData(8675309, false, "1544E2EE20E8A8A0FA9E6417ACEB103BD875B1334179571D938D069102154AE8", 187, false)]
    [InlineData(42, false, "1FCE76F0ADD40A1C255E2ED05DD53DD76F001F9EFA3E7FE6F1838DC8C9E5D5A0", 187, true)]
    [InlineData(1458, false, "93390636832C33F6F2C8FFD157D65087EB9EE41E330A104A65C1B9D41AB3001E", 187, true)]
    [InlineData(8675309, false, "C60591D61240A20206FE819F89426D599F9A7125E8B970C0D78542232A7757C6", 187, true)]
    [InlineData(42, false, "651546C4FD72345D13742C08A56497EA24A6692D2A71330398387E8BF2557518", 216, false)]
    [InlineData(1458, false, "2C89CD35F05FF845082B5117CE47B204CA22B117289FA1F6769B50CC7F130C61", 216, false)]
    [InlineData(8675309, false, "BC1D03FDFD69AAD8518CDE86B8F85BB928FE0A9A720D8EF70BE608BBA312856E", 216, false)]
    [InlineData(42, false, "1DF88FD37B48080B04CBA22ED5553B4D488CC27D1EF7B37AF5C6B502EB7C899B", 216, true)]
    [InlineData(1458, false, "174BBB8E34C1076877A47036647331BE4C3B09D85FDF94090A552C2512A192CA", 216, true)]
    [InlineData(8675309, false, "0359228EC11846704FAB0CE4162B6688402F0F1D16FB899A39AFC2B71AE4122A", 216, true)]
    [InlineData(42, false, "645810577C99EECFE119891EEB37C639B6A2E764734314471845CA3B10EBBA63", 186, false)]
    [InlineData(1458, false, "A018A112D811F653C51737DA51AE908BD6381C55F98D3E26661119EC8595F35A", 186, false)]
    [InlineData(8675309, false, "D6E2EE5F664AEDE61BC3DAC1869A95641339D731D7A394DF93BF26CBEC82BCA8", 186, false)]
    [InlineData(42, false, "2D91E7E1318B01717C37E30157A11D6FD060C2E37F7328BBC642165AFDB6E0A1", 186, true)]
    [InlineData(1458, false, "77C943BB427BE519F8148D836CA5F8BFF19007CE69191D983E8A35BA67ED2DAA", 186, true)]
    [InlineData(8675309, false, "945A734E0816F8778D31CC2FA227466B22F4FE9805F3B935B83EF77589FD2103", 186, true)]
    public void Mixed_contacts_match_official_cells(int seed, bool embedded, string hash, ushort wall = 0, bool loading = false)
    {
        var store = new WorldTileStore(new WorldDimensions(100, 400));
        var random = new VanillaUnifiedRandom1458(seed);
        for (int x = 0; x < 100; x++)
        for (int y = 0; y < 400; y++)
        {
            bool solid = x < 10 || x > 89 || y > 389 || y < 100 || random.Next(5) == 0;
            _ = random.Next(6); // Retained material draw from the independent mixed-material fixture.
            var tile = new WorldTile { Type = 1, Wall = wall, Flags = solid ? WorldTileFlags.Active : 0 };
            if ((!solid || embedded) && random.Next(10) == 0)
            {
                tile.LiquidAmount = (byte)random.Next(1, 256);
                tile.LiquidKind = (WorldLiquidKind)random.Next(4);
            }
            store.Tiles[store.GetUncheckedIndex(x, y)] = tile;
        }
        if (loading) new VanillaWorldLiquidSimulator1458(store).QuickWater();
        else UnderworldLiquidPreparation1458.Settle(store, 180, TestContext.Current.CancellationToken);
        var cells = new WorldTile[40000];
        for (int x = 0; x < 100; x++)
        for (int y = 0; y < 400; y++) cells[x * 400 + y] = store.Get(x, y);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(cells.AsSpan()))));
    }
}
