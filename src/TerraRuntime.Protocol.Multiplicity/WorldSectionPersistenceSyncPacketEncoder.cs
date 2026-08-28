using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public enum WorldSectionPersistenceSyncPacketEncodeResult : byte
{
    Encoded = 0,
    InvalidNpc = 1,
    InvalidChest = 2
}

/// <summary>
/// Encodes the persistence-backed frames emitted immediately after one Terraria packet-10 section.
/// Vanilla ordering is town-NPC packet 23 first, then chest size/content synchronization.
/// </summary>
public static class WorldSectionPersistenceSyncPacketEncoder
{
    public static WorldSectionPersistenceSyncPacketEncodeResult TryEncode(
        WorldDimensions dimensions,
        IReadOnlyList<WorldTownNpc> townNpcs,
        IReadOnlyList<WorldChest> chests,
        WorldSectionId section,
        out ReadOnlyMemory<byte>[] frames)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(townNpcs);
        ArgumentNullException.ThrowIfNull(chests);
        TerrariaSectionGeometry.ValidateSection(dimensions, section);

        var encodedFrames = new List<ReadOnlyMemory<byte>>();

        for (int npcSlot = 0; npcSlot < townNpcs.Count; npcSlot++)
        {
            WorldTownNpc npc = townNpcs[npcSlot];
            int tileX = (int)(npc.X / 16f);
            int tileY = (int)(npc.Y / 16f);
            if (tileX / TerrariaSectionGeometry.WidthTiles != section.X ||
                tileY / TerrariaSectionGeometry.HeightTiles != section.Y)
            {
                continue;
            }

            WorldTownNpcSyncPacketEncodeResult npcResult = WorldTownNpcSyncPacketEncoder.TryEncode(
                npcSlot,
                npc,
                out ReadOnlyMemory<byte> npcFrame);
            if (npcResult != WorldTownNpcSyncPacketEncodeResult.Encoded)
            {
                frames = [];
                return WorldSectionPersistenceSyncPacketEncodeResult.InvalidNpc;
            }

            encodedFrames.Add(npcFrame);
        }

        foreach (WorldChest chest in chests)
        {
            if ((uint)chest.X >= (uint)dimensions.WidthTiles ||
                (uint)chest.Y >= (uint)dimensions.HeightTiles)
            {
                frames = [];
                return WorldSectionPersistenceSyncPacketEncodeResult.InvalidChest;
            }

            if (TerrariaSectionGeometry.FromTile(dimensions, chest.X, chest.Y) != section)
                continue;

            WorldChestSyncPacketEncodeResult chestResult = WorldChestSyncPacketEncoder.TryEncode(
                chest,
                out ReadOnlyMemory<byte>[] chestFrames);
            if (chestResult != WorldChestSyncPacketEncodeResult.Encoded)
            {
                frames = [];
                return WorldSectionPersistenceSyncPacketEncodeResult.InvalidChest;
            }

            encodedFrames.AddRange(chestFrames);
        }

        frames = encodedFrames.ToArray();
        return WorldSectionPersistenceSyncPacketEncodeResult.Encoded;
    }
}
