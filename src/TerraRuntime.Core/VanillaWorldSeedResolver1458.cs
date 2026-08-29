using System.Globalization;
using System.Text;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Terraria 1.4.5.8 world-generator integer seed boundary. Numeric Int32 seed text is used verbatim; other text is
/// converted through the standard CRC-32 polynomial used by Terraria's seed path. The original text remains on the
/// request and is persisted unchanged by the .wld creation pipeline.
/// </summary>
internal static class VanillaWorldSeedResolver1458
{
    public static int Resolve(in WorldGenerationRequest request)
    {
        string text = request.SeedText ?? request.Seed.ToString(CultureInfo.InvariantCulture);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
            return numeric;

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1u));
        }

        return unchecked((int)~crc);
    }
}
