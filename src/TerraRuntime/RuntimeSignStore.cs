using System.Text;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Game-thread-owned protocol-326 sign table. Runtime slot identity follows TerrariaServer 1.4.5.8 while the server is
/// running: packet 46 resolves/allocates the first free slot and packet 47 may replace any slot in the vanilla range.
/// Canonical .wld persistence intentionally compacts non-null runtime slots in ascending slot order because vanilla
/// SaveSigns does not serialize slot ids and LoadSigns restores saved entries contiguously from slot zero.
/// </summary>
internal sealed class RuntimeSignStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly WorldSign?[] signs = new WorldSign?[VanillaWorldFormat326.MaximumSignSlots];
    private readonly WorldTileStore? tiles;

    public RuntimeSignStore(ReadOnlySpan<WorldSign> source, WorldTileStore? tiles = null)
    {
        if (source.Length > signs.Length)
            throw new ArgumentOutOfRangeException(nameof(source));

        this.tiles = tiles;
        foreach (WorldSign sourceSign in source)
        {
            WorldSign sign = sourceSign ?? throw new ArgumentNullException(nameof(source));
            if (sign.SlotId < 0 || sign.SlotId >= signs.Length)
                throw new ArgumentOutOfRangeException(nameof(source), $"Sign slot {sign.SlotId} is outside the vanilla range.");
            if (signs[sign.SlotId] is not null)
                throw new InvalidOperationException($"Duplicate sign slot {sign.SlotId} in loaded world state.");

            signs[sign.SlotId] = new WorldSign(sign.SlotId, sign.Text ?? string.Empty, sign.X, sign.Y);
        }
    }

    public bool CanPersistMutations => true;

    public bool TryRead(short tileX, short tileY, out WorldSign sign)
    {
        int x = tileX;
        int y = tileY;
        if (tiles is not null)
        {
            WorldDimensions dimensions = tiles.Dimensions;
            if ((uint)x >= (uint)dimensions.WidthTiles ||
                (uint)y >= (uint)dimensions.HeightTiles ||
                !VanillaSignTileResolver.TryResolve(tiles, x, y, out x, out y))
            {
                sign = null!;
                return false;
            }
        }

        for (short id = 0; id < signs.Length; id++)
        {
            if (signs[id] is WorldSign existing && existing.X == x && existing.Y == y)
            {
                sign = existing;
                return true;
            }
        }

        // Sign.ReadSign(CreateIfMissing: true) allocates the first free runtime slot. Without tile state we can still
        // service loaded-sign lookups, but we deliberately cannot validate or create a new sign object.
        if (tiles is not null)
        {
            for (short id = 0; id < signs.Length; id++)
            {
                if (signs[id] is not null)
                    continue;

                sign = new WorldSign(id, string.Empty, x, y);
                signs[id] = sign;
                return true;
            }
        }

        sign = null!;
        return false;
    }

    /// <summary>
    /// Applies TerrariaServer 1.4.5.8 packet-47 semantics. A valid slot id replaces the previous sign object with the
    /// submitted coordinates before TextSign validation. Invalid/inactive sign coordinates therefore clear that slot.
    /// <paramref name="textChanged"/> mirrors vanilla's observer-broadcast condition: only old text versus submitted
    /// text is compared, so a coordinate-only replacement is persisted without an observer broadcast.
    /// </summary>
    public bool TryApply(
        in TerrariaSignState submitted,
        out WorldSign? committed,
        out bool textChanged)
    {
        committed = null;
        textChanged = false;
        if (submitted.SignId < 0 ||
            submitted.SignId >= signs.Length ||
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

        int id = submitted.SignId;
        string? previousText = signs[id]?.Text;

        if (tiles is null)
        {
            // Tests and non-host callers without authoritative tile state may update an already-known exact sign, but
            // creation/movement cannot be validated and therefore fails closed. Production always supplies tiles.
            if (signs[id] is not WorldSign existing ||
                existing.X != submitted.TileX ||
                existing.Y != submitted.TileY)
            {
                return false;
            }

            committed = new WorldSign(existing.SlotId, submitted.Text, existing.X, existing.Y);
            signs[id] = committed;
            textChanged = !string.Equals(previousText, submitted.Text, StringComparison.Ordinal);
            return true;
        }

        WorldDimensions dimensions = tiles.Dimensions;
        int x = submitted.TileX;
        int y = submitted.TileY;
        if ((uint)x >= (uint)dimensions.WidthTiles ||
            (uint)y >= (uint)dimensions.HeightTiles)
        {
            return false;
        }

        WorldTile tile = tiles.Get(x, y);
        bool validSignTile =
            (tile.Flags & WorldTileFlags.Active) != 0 &&
            VanillaSignTileResolver.IsSignTileType(tile.Type);

        if (validSignTile)
        {
            committed = new WorldSign(submitted.SignId, submitted.Text, x, y);
            signs[id] = committed;
        }
        else
        {
            // Sign.TextSign clears Main.sign[id] when the submitted coordinates do not point at an active sign tile.
            signs[id] = null;
        }

        textChanged = !string.Equals(previousText, submitted.Text, StringComparison.Ordinal);
        return true;
    }

    /// <summary>
    /// Captures the exact semantic image produced by vanilla SaveSigns followed by LoadSigns: all non-null runtime
    /// slots are emitted in ascending slot order and receive compact slot ids 0..N-1 after the next load.
    /// </summary>
    public bool TryCaptureCanonicalSnapshot(out WorldSign[] snapshot)
    {
        var compacted = new List<WorldSign>();
        for (int id = 0; id < signs.Length; id++)
        {
            if (signs[id] is not WorldSign sign)
                continue;

            short persistedId = checked((short)compacted.Count);
            compacted.Add(new WorldSign(persistedId, sign.Text, sign.X, sign.Y));
        }

        snapshot = compacted.ToArray();
        return true;
    }
}
