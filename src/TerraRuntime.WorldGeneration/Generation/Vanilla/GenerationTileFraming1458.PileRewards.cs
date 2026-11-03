namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private void EvaluateSuppressedPileDrops(ushort type, short frameX)
    {
        // Source Check3x2 evaluates stack arguments before Item.NewItem's generation refusal.
        // Keep each conditional stack choice, even though the cold world never receives those items.
        if (type == 187 && frameX is >= 918 and <= 970)
        {
            _ = random.Next(30);
            return;
        }
        if (type != 186 || frameX is < 864 or > 1170) return;
        if (frameX <= 954)
        {
            for (int minimum = 20; minimum <= 40; minimum += 10) _ = random.Next(minimum, 100);
            for (int group = 0; group < 5; group++)
            {
                int divisor = group switch { 0 => 3, 1 => 2, 2 => 3, 3 => 4, _ => 5 };
                for (int minimum = 20; minimum <= 40; minimum += 10)
                {
                    int offer = random.Next(divisor);
                    if (group == 0 ? offer != 0 : offer == 0) _ = random.Next(minimum, 100);
                }
            }
            return;
        }
        bool gold = frameX > 1062;
        _ = random.Next(gold ? 1 : 10, gold ? 7 : 100);
        for (int divisor = 2; divisor <= 5; divisor++)
            if (random.Next(divisor) == 0) _ = random.Next(gold ? divisor : divisor * 10, gold ? 7 : 100);
    }
}
