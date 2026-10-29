using System.IO.Compression;
using System.Text.Json;
using global::Multiplicity.Packets;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class NpcSpawnShimmer1458Tests
{
    public static IEnumerable<object[]> OriginalPackets()
    {
        using var stream=typeof(NpcSpawnShimmer1458Tests).Assembly.GetManifestResourceStream("NpcSpawnShimmerFlagsLinux1458.v1")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var document=JsonDocument.Parse(gzip);
        foreach(var row in document.RootElement.GetProperty("cases").EnumerateArray())yield return [row.GetRawText()];
    }

    [Theory,MemberData(nameof(OriginalPackets))]
    public void Source_powder_checkpoints_preserve_the_actual_spawn_shimmer_bit_and_every_other_packet_byte(string json)
    {
        using var document=JsonDocument.Parse(json);var row=document.RootElement;var e=row.GetProperty("npc");
        var source=new NpcSnapshot(new(0,new(e.GetProperty("generation").GetUInt64())),new(1),
            e.GetProperty("type").GetInt32(),e.GetProperty("netId").GetInt16(),
            F(e.GetProperty("position")[0]),F(e.GetProperty("position")[1]),F(e.GetProperty("velocity")[0]),F(e.GetProperty("velocity")[1]),
            e.GetProperty("target").GetUInt16(),Ai(e.GetProperty("ai")),NpcSimulationState.Initial with {
                Life=e.GetProperty("life").GetInt32(),LifeMax=e.GetProperty("lifeMax").GetInt32(),
                DirectionX=e.GetProperty("direction").GetInt32(),DirectionY=e.GetProperty("directionY").GetInt32(),
                SpriteDirection=e.GetProperty("spriteDirection").GetInt32(),Scale=F(e.GetProperty("scale")),
                HitboxOverride=new(e.GetProperty("width").GetInt32(),e.GetProperty("height").GetInt32()),
                SpawnedFromStatue=e.GetProperty("spawnedFromStatue").GetBoolean(),
                ShimmerTransparency=F(e.GetProperty("shimmer")),SpawnDifficulty=1f,ReleaseOwner=255 });
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in source,RuntimeNpcSyncKind.Spawn,out var state));
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in state,out var actual));
        Assert.Equal(Convert.FromHexString(row.GetProperty("expectedFrame").GetString()!),actual);
        Assert.Equal(F(e.GetProperty("shimmer"))>0f,state.SpawnShimmered);
    }

    [Theory]
    [InlineData(false,0f,false)]
    [InlineData(false,.375f,false)]
    [InlineData(true,0f,false)]
    [InlineData(true,.375f,true)]
    [InlineData(true,float.Epsilon,true)]
    public void Source_bit4_requires_both_pending_birth_and_strictly_positive_transparency(bool pending,float transparency,bool expected)
    {
        var npc=Npc(transparency);
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc,RuntimeNpcSyncKind.Update,out var state));
        // The existing catch journal supplies its source pending-birth flag after projection.
        state=state with {SpawnNeedsSyncing=pending};
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in state,out var bytes));
        var decoded=Assert.IsType<NpcUpdate>(TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)bytes));
        Assert.Equal(expected,(decoded.ExtraFlags&NpcUpdateExtraFlags.HasShimmerTransparency)!=0);
    }

    [Fact]
    public void Spawn_shimmer_flag_preserves_statue_difficulty_and_birth_flags_without_an_extra_payload()
    {
        var npc=Npc(.375f);
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc,RuntimeNpcSyncKind.Spawn,out var state));
        state=state with {SpawnedFromStatue=true,SpawnDifficulty=2f};
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in state,out var bytes));
        var decoded=Assert.IsType<NpcUpdate>(TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)bytes));
        Assert.Equal(NpcUpdateExtraFlags.SpawnedFromStatue|NpcUpdateExtraFlags.HasDifficultyOverride|
            NpcUpdateExtraFlags.SpawnNeedsSyncing|NpcUpdateExtraFlags.HasShimmerTransparency,decoded.ExtraFlags);
        var zero=state with {SpawnShimmered=false};
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in zero,out var without));
        Assert.Equal(without.Length,bytes.Length);
    }

    [Fact]
    public void Unknown_legacy_transparency_keeps_its_existing_unqualified_projection()
    {
        // Preserving the legacy wire fallback does not admit an unknown or nonzero Shimmer AI phase.
        var npc=Npc(null);
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc,RuntimeNpcSyncKind.Spawn,out var state));
        Assert.False(state.SpawnShimmered);
    }

    private static NpcSnapshot Npc(float? transparency)=>new(new(0,new(1)),new(1),534,534,1023,1560,0,0,255,default,
        NpcSimulationState.Initial with {Life=400,LifeMax=400,DirectionX=1,DirectionY=-1,SpriteDirection=1,ShimmerTransparency=transparency});
    private static float F(JsonElement value)=>BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(),16));
    private static NpcAiState Ai(JsonElement values)=>new(F(values[0]),F(values[1]),F(values[2]),F(values[3]));
}
