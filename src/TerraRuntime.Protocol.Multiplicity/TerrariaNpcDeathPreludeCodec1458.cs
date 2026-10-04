using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaBannerMessage1458 : byte { FullState, KillCount, ClaimCount, ClaimRequest, ClaimResponse }
public readonly record struct TerrariaBannerClaim1458(short Banner, ushort Amount);

/// <summary>Official NetworkInitializer module11 BannerSystem and module4 Bestiary wire boundaries.</summary>
public static class TerrariaNpcDeathPreludeCodec1458
{
    public const ushort BannerModuleId = 11;
    public const ushort BestiaryModuleId = 4;
    public const int BannerCount = 293;
    public static bool IsBannerFrame(in TerrariaFrame frame)
    {
        if (frame.MessageId != (byte)TerrariaMessageId.LoadNetModule || frame.Payload.Length < sizeof(ushort)) return false;
        Span<byte> id = stackalloc byte[sizeof(ushort)]; frame.Payload.Slice(0, sizeof(ushort)).CopyTo(id);
        return BinaryPrimitives.ReadUInt16LittleEndian(id) == BannerModuleId;
    }
    public static bool TryDecodeClaim(in TerrariaFrame frame, out TerrariaBannerClaim1458 request)
    {
        request = default;
        if (!IsBannerFrame(in frame) || frame.Payload.Length != 7) return false;
        Span<byte> payload = stackalloc byte[7]; frame.Payload.CopyTo(payload);
        if (payload[2] != (byte)TerrariaBannerMessage1458.ClaimRequest) return false;
        short banner = BinaryPrimitives.ReadInt16LittleEndian(payload[3..]);
        if ((uint)banner >= BannerCount) return false;
        request = new(banner, BinaryPrimitives.ReadUInt16LittleEndian(payload[5..])); return true;
    }
    public static byte[] EncodeBannerFull(ReadOnlySpan<int> kills, ReadOnlySpan<ushort> claims)
    {
        if (kills.Length > BannerCount || claims.Length > BannerCount) throw new ArgumentOutOfRangeException(nameof(kills));
        byte[] body = new byte[5 + kills.Length * sizeof(int) + claims.Length * sizeof(ushort)];
        body[0] = (byte)TerrariaBannerMessage1458.FullState;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), checked((short)kills.Length)); int offset = 3;
        foreach (int value in kills) { BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), value); offset += sizeof(int); }
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(offset), checked((short)claims.Length)); offset += sizeof(short);
        foreach (ushort value in claims) { BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), value); offset += sizeof(ushort); }
        return EncodeModule(BannerModuleId, body);
    }
    public static byte[] EncodeBannerKill(short banner, int kills) => EncodeBannerCounter(banner, kills, null);
    public static byte[] EncodeBannerClaimCount(short banner, ushort count) => EncodeBannerCounter(banner, 0, count);
    private static byte[] EncodeBannerCounter(short banner, int kills, ushort? claims)
    {
        if ((uint)banner >= BannerCount) throw new ArgumentOutOfRangeException(nameof(banner));
        Span<byte> body = stackalloc byte[claims.HasValue ? 5 : 7];
        body[0] = (byte)(claims.HasValue ? TerrariaBannerMessage1458.ClaimCount : TerrariaBannerMessage1458.KillCount);
        BinaryPrimitives.WriteInt16LittleEndian(body[1..], banner);
        if (claims.HasValue) BinaryPrimitives.WriteUInt16LittleEndian(body[3..], claims.Value);
        else BinaryPrimitives.WriteInt32LittleEndian(body[3..], kills);
        return EncodeModule(BannerModuleId, body);
    }
    public static byte[] EncodeClaimRequest(short banner, ushort amount)
    {
        if ((uint)banner >= BannerCount) throw new ArgumentOutOfRangeException(nameof(banner));
        Span<byte> body = stackalloc byte[5]; body[0] = (byte)TerrariaBannerMessage1458.ClaimRequest;
        BinaryPrimitives.WriteInt16LittleEndian(body[1..], banner); BinaryPrimitives.WriteUInt16LittleEndian(body[3..], amount);
        return EncodeModule(BannerModuleId, body);
    }
    public static byte[] EncodeClaimResponse(short banner, ushort amount, bool granted)
    {
        if ((uint)banner >= BannerCount) throw new ArgumentOutOfRangeException(nameof(banner));
        Span<byte> body = stackalloc byte[6]; body[0] = (byte)TerrariaBannerMessage1458.ClaimResponse;
        BinaryPrimitives.WriteInt16LittleEndian(body[1..], banner); BinaryPrimitives.WriteUInt16LittleEndian(body[3..], amount); body[5] = granted ? (byte)1 : (byte)0;
        return EncodeModule(BannerModuleId, body);
    }
    public static byte[] EncodeBestiaryKill(short netId, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); writer.Write(netId); writer.Write7BitEncodedInt(count);
        return EncodeModule(BestiaryModuleId, stream.ToArray());
    }
    public static byte[] EncodeNpcKillAchievement(short netId)
    {
        Span<byte> payload = stackalloc byte[sizeof(short)]; BinaryPrimitives.WriteInt16LittleEndian(payload, netId);
        return EncodeFrame((byte)TerrariaMessageId.NpcKillAchievement, payload);
    }
    public static byte[] EncodeBestiarySight(short netId) => EncodeBestiaryFlag(netId, 1);
    public static byte[] EncodeBestiaryChat(short netId) => EncodeBestiaryFlag(netId, 2);
    private static byte[] EncodeBestiaryFlag(short netId, byte kind)
    {
        Span<byte> body = stackalloc byte[3]; body[0] = kind;
        BinaryPrimitives.WriteInt16LittleEndian(body[1..], netId);
        return EncodeModule(BestiaryModuleId, body);
    }
    private static byte[] EncodeModule(ushort module, ReadOnlySpan<byte> body)
    {
        byte[] payload = new byte[sizeof(ushort) + body.Length]; BinaryPrimitives.WriteUInt16LittleEndian(payload, module); body.CopyTo(payload.AsSpan(sizeof(ushort)));
        return EncodeFrame((byte)TerrariaMessageId.LoadNetModule, payload);
    }
    private static byte[] EncodeFrame(byte message, ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length];
        if (TerrariaFrameEncoder.TryWrite(frame, message, payload) != TerrariaFrameWriteResult.Written) throw new InvalidOperationException("Bounded prelude frame encoding failed.");
        return frame;
    }
}
