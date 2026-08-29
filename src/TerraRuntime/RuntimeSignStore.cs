using System.Text;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Game-thread-owned projection of loaded world signs. The first runtime slice deliberately does not allocate or
/// remove sign slots: existing signs can be read, while text mutation is admitted only when the loaded sign table is
/// persistence-canonical (slot id equals file-order index). This prevents a sparse/corrupt source table from being
/// silently renumbered by a later save.
/// </summary>
internal sealed class RuntimeSignStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly WorldSign?[] signs = new WorldSign?[VanillaWorldFormat326.MaximumSignSlots];
    private readonly Dictionary<long, short> signByCoordinates = [];
    private readonly bool persistenceCanonical;
    private readonly int sourceCount;

    public RuntimeSignStore(ReadOnlySpan<WorldSign> source)
    {
        if (source.Length > signs.Length)
            throw new ArgumentOutOfRangeException(nameof(source));

        bool canonical = true;
        for (int index = 0; index < source.Length; index++)
        {
            WorldSign sign = source[index] ?? throw new ArgumentNullException(nameof(source));
            if (sign.SlotId < 0 || sign.SlotId >= signs.Length)
                throw new ArgumentOutOfRangeException(nameof(source), $"Sign slot {sign.SlotId} is outside the vanilla range.");
            if (signs[sign.SlotId] is not null)
                throw new InvalidOperationException($"Duplicate sign slot {sign.SlotId} in loaded world state.");

            long key = GetCoordinateKey(sign.X, sign.Y);
            if (!signByCoordinates.TryAdd(key, sign.SlotId))
                throw new InvalidOperationException($"Duplicate runtime sign coordinates {sign.X},{sign.Y}.");

            canonical &= sign.SlotId == index;
            signs[sign.SlotId] = new WorldSign(sign.SlotId, sign.Text ?? string.Empty, sign.X, sign.Y);
        }

        sourceCount = source.Length;
        persistenceCanonical = canonical;
    }

    public bool CanPersistMutations => persistenceCanonical;

    public bool TryRead(short tileX, short tileY, out WorldSign sign)
    {
        int x = tileX;
        int y = tileY;

        if (TryGetAt(x, y, out sign) ||
            TryGetAt(x - 1, y, out sign) ||
            TryGetAt(x, y - 1, out sign) ||
            TryGetAt(x - 1, y - 1, out sign))
        {
            return true;
        }

        sign = null!;
        return false;
    }

    public bool TryApply(
        in TerrariaSignState submitted,
        out WorldSign committed,
        out bool changed)
    {
        committed = null!;
        changed = false;
        if (!persistenceCanonical ||
            submitted.SignId < 0 ||
            submitted.SignId >= signs.Length ||
            signs[submitted.SignId] is not WorldSign existing ||
            existing.X != submitted.TileX ||
            existing.Y != submitted.TileY ||
            submitted.Text is null)
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetByteCount(submitted.Text);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        if (string.Equals(existing.Text, submitted.Text, StringComparison.Ordinal))
        {
            committed = existing;
            return true;
        }

        var updated = new WorldSign(existing.SlotId, submitted.Text, existing.X, existing.Y);
        signs[existing.SlotId] = updated;
        committed = updated;
        changed = true;
        return true;
    }

    /// <summary>
    /// Captures a detached, canonical persistence image. Sparse source slot identities are intentionally rejected;
    /// callers should continue preserving the original opaque sign section for such worlds until a lossless sparse
    /// representation is available.
    /// </summary>
    public bool TryCaptureCanonicalSnapshot(out WorldSign[] snapshot)
    {
        if (!persistenceCanonical)
        {
            snapshot = [];
            return false;
        }

        snapshot = new WorldSign[sourceCount];
        for (int index = 0; index < sourceCount; index++)
        {
            if (signs[index] is not WorldSign sign || sign.SlotId != index)
                throw new InvalidOperationException("Runtime sign table lost canonical slot identity.");

            snapshot[index] = new WorldSign(sign.SlotId, sign.Text, sign.X, sign.Y);
        }
        return true;
    }

    private bool TryGetAt(int x, int y, out WorldSign sign)
    {
        if (signByCoordinates.TryGetValue(GetCoordinateKey(x, y), out short id) &&
            signs[id] is WorldSign existing)
        {
            sign = existing;
            return true;
        }

        sign = null!;
        return false;
    }

    private static long GetCoordinateKey(int x, int y) =>
        ((long)(uint)x << 32) | (uint)y;
}
