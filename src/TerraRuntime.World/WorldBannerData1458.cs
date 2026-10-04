using System.Buffers.Binary;
namespace TerraRuntime.World;

/// <summary>Detached SaveWorldFlags BannerSystem state. Vanilla v326 persists both arrays independently.</summary>
public sealed record WorldBannerData1458(int[] KillCounts, ushort[] ClaimableCounts)
{
    public const int MaximumEntries = 293;
    public static WorldBannerData1458 Empty => new([], []);
    public bool IsValid => KillCounts is not null && ClaimableCounts is not null &&
        KillCounts.Length <= MaximumEntries && ClaimableCounts.Length <= MaximumEntries;
    public WorldBannerData1458 Copy() => new((int[])KillCounts.Clone(), (ushort[])ClaimableCounts.Clone());
    public byte[] Encode()
    {
        if (!IsValid) throw new InvalidDataException("Invalid bounded banner state.");
        byte[] result = new byte[4 + KillCounts.Length * sizeof(int) + ClaimableCounts.Length * sizeof(ushort)];
        BinaryPrimitives.WriteInt16LittleEndian(result, checked((short)KillCounts.Length));
        int offset = sizeof(short);
        foreach (int count in KillCounts) { BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), count); offset += sizeof(int); }
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(offset), checked((short)ClaimableCounts.Length)); offset += sizeof(short);
        foreach (ushort count in ClaimableCounts) { BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), count); offset += sizeof(ushort); }
        return result;
    }
}
