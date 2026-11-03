using System.Security.Cryptography;
using TerraRuntime.Application;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;
using TerraRuntime.Tests.DurableWorldChestCustody;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldInitialBackup1458Tests
{
    [Theory]
    [InlineData(2u,1u)]
    [InlineData(4096u,4095u)]
    public void First_checkpoint_replaces_unmanaged_vanilla_backup_without_authorizing_it(uint current, uint previous)
    {
        using var f=new Fixture(current,previous);
        string old=System.IO.Path.Combine(f.Directory,"unmanaged-old.wld");
        File.Copy(f.Path+".bak",old);
        Assert.Throws<FileNotFoundException>(()=>f.Artifacts.ValidateRecoveryCandidateAsync(old,TestContext.Current.CancellationToken).GetAwaiter().GetResult());
        byte[] canonical=File.ReadAllBytes(f.Path);
        f.Save(f.Capture());
        Assert.Equal(canonical,File.ReadAllBytes(f.Path+".bak"));
        Assert.Equal((short)1,f.Read().Chests[0].Quantity[0]);
        Assert.Equal(100L,f.Read().Chests[0].Copper[1]);
        Assert.Throws<FileNotFoundException>(()=>f.Artifacts.ValidateRecoveryCandidateAsync(old,TestContext.Current.CancellationToken).GetAwaiter().GetResult());
        Assert.Equal(2,System.IO.Directory.GetFiles(f.Path+".custody","generation-*.bin").Length);
    }

    [Fact]
    public void Corrupt_existing_backup_authority_is_not_treated_as_an_unmanaged_backup()
    {
        using var f=new Fixture(2,1);
        byte[] raw=File.ReadAllBytes(f.Path+".bak");var world=f.Load(raw);byte[] hash=SHA256.HashData(raw);
        byte[] encoded=RuntimeWorldChestCustodyCodec.Encode(RuntimeWorldChestCustodySnapshot.FullImport(world),hash);
        encoded[^1]^=1;
        File.WriteAllBytes(f.Artifacts.ArtifactPath(world.Envelope.Revision,hash),encoded);
        Assert.Throws<InvalidDataException>(()=>f.Save(f.Capture()));
        Assert.Equal(2u,f.Load(File.ReadAllBytes(f.Path)).Envelope.Revision);
    }

    [Fact]
    public void Later_managed_backup_with_missing_authority_still_blocks_publication()
    {
        using var f=new Fixture(2,1);f.Save(f.Capture());
        byte[] backup=File.ReadAllBytes(f.Path+".bak");var world=f.Load(backup);
        File.Delete(f.Artifacts.ArtifactPath(world.Envelope.Revision,SHA256.HashData(backup)));
        byte[] canonical=File.ReadAllBytes(f.Path);
        Assert.Throws<FileNotFoundException>(()=>f.Save(f.Capture()));
        Assert.Equal(canonical,File.ReadAllBytes(f.Path));
        Assert.Equal((short)1,f.Read().Chests[0].Quantity[0]);
        Assert.Throws<InvalidDataException>(()=>f.Artifacts.InitializeTrustedImportAsync(f.Load(canonical),true,TestContext.Current.CancellationToken).GetAwaiter().GetResult());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Malformed_or_foreign_unmanaged_backup_still_blocks_preparation(bool foreign)
    {
        using var f=new Fixture(2,1);
        File.WriteAllBytes(f.Path+".bak",foreign?f.World(1,foreign:true):new byte[]{1,2,3});
        if(foreign)Assert.NotEqual(f.Load(File.ReadAllBytes(f.Path)).Header.UniqueId,
            f.Load(File.ReadAllBytes(f.Path+".bak")).Header.UniqueId);
        byte[] canonical=File.ReadAllBytes(f.Path);
        Assert.Throws<InvalidDataException>(()=>f.Save(f.Capture()));
        Assert.Equal(canonical,File.ReadAllBytes(f.Path));
    }

    [Fact]
    public void Failed_preparation_preserves_vanilla_backup_then_next_checkpoint_cleans_only_its_orphan()
    {
        using var f=new Fixture(2,1);byte[] canonical=File.ReadAllBytes(f.Path),backup=File.ReadAllBytes(f.Path+".bak");
        var rejected=f.Capture();
        Assert.Throws<IOException>(()=>f.Save(rejected,failAfterPrepare:true));
        Assert.Equal(canonical,File.ReadAllBytes(f.Path));Assert.Equal(backup,File.ReadAllBytes(f.Path+".bak"));
        Assert.True(File.Exists(f.Artifacts.ArtifactPath(rejected.Revision.Value,SHA256.HashData(f.World(rejected.Revision.Value)))));
        f.Save(f.Capture());
        Assert.False(File.Exists(f.Artifacts.ArtifactPath(rejected.Revision.Value,SHA256.HashData(f.World(rejected.Revision.Value)))));
        Assert.Equal((short)1,f.Read().Chests[0].Quantity[0]);
    }

    private sealed class Fixture:IDisposable
    {
        private readonly WorldFileLoadLimits limits=WorldFixture.CreateLimits();
        private readonly WorldFileData source;
        private readonly WorldFilePreservedSections preserved;
        private readonly WorldTileSaveImage tiles;
        private readonly WorldPersistenceOwnerLease owner;
        public string Directory{get;}
        public string Path{get;}
        public RuntimeWorldChestCustodyArtifacts Artifacts{get;}
        public Fixture(uint current,uint previous)
        {
            byte[] bytes=WorldFixture.CreateCompleteCurrentWorld();source=Load(bytes);
            Assert.True(WorldFilePreservedSections.TryCapture(bytes,source.Envelope,out var captured));preserved=captured!;
            var shadow=new WorldTileSaveShadowSynchronizer(source.Tiles,1);shadow.CaptureBootstrap(1);
            Assert.True(shadow.TryCaptureImage(out var image));tiles=image!;
            Directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"TerraRuntime.Tests","InitialBackup",Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);Path=System.IO.Path.Combine(Directory,"canonical.wld");
            File.WriteAllBytes(Path,World(current));File.WriteAllBytes(Path+".bak",World(previous));
            owner=WorldPersistenceOwnerLease.Acquire(Path);owner.InitializeRevision(current);
            Artifacts=new(owner,limits,1_000_000);
            Artifacts.InitializeTrustedImportAsync(Load(File.ReadAllBytes(Path)),true).GetAwaiter().GetResult();
        }
        public WorldFileData Load(byte[] bytes)
        {
            Assert.True(WorldFileLoader.TryLoad(bytes,limits,out var world).IsLoaded);return world!;
        }
        public byte[] World(uint revision,bool foreign=false)
        {
            var envelope=new WorldFileEnvelope(source.Envelope.FormatVersion,revision,source.Envelope.FavoriteFlags,
                source.Envelope.SectionOffsets.ToArray(),source.Envelope.FrameImportanceCount,source.Envelope.FrameImportanceBits.ToArray());
            var header=foreign?source.Header with{UniqueId=new Guid("11111111-2222-3333-4444-555555555555")}:source.Header;
            WorldChest[] chests=[new(0,0,0,"custody",[new(3,9,0),new(3,72,0),default])];
            using var prefix=new MemoryStream();
            Assert.Equal(WorldFileHeaderPrefixEncodeResult.Encoded,
                WorldFileHeaderPrefixEncoder.TryEncode(header,prefix,out _));
            byte[] patchedHeader=preserved.Header.ToArray();prefix.ToArray().CopyTo(patchedHeader,0);
            using var stream=new MemoryStream();
            Assert.Equal(WorldFileTileChestRewriteResult.Rewritten,
                WorldFileTileChestRewriter.TryRewrite(envelope,header,patchedHeader,preserved,tiles,chests,stream,out _));
            return stream.ToArray();
        }
        public RuntimeWorldCustodyCheckpoint Capture()
        {
            var revision=owner.ReserveNextRevision();var custody=RuntimeWorldChestCustodySnapshot.FullImport(Load(World(revision.Value)));
            custody.Chests[0].Quantity[0]=1;custody.Chests[0].Copper[1]=100;return new(custody,revision);
        }
        public void Save(RuntimeWorldCustodyCheckpoint checkpoint,bool failAfterPrepare=false)
        {
            byte[] bytes=World(checkpoint.Revision.Value);
            using var write=owner.EnterCheckpointWrite();
            var options=new AtomicSaveFileWriterOptions(BackupPath:Path+".bak",
                ValidateCandidateAsync:async(candidate,token)=>{
                    await Artifacts.PrepareCandidateAsync(checkpoint,candidate,token);
                    if(failAfterPrepare)throw new IOException("Injected after prepared dependency, before publication");
                },ValidateBackupAsync:Artifacts.ValidateRecoveryCandidateAsync);
            AtomicSaveFileWriter.WriteAsync(Path,(stream,token)=>stream.WriteAsync(bytes,token).AsTask(),options).GetAwaiter().GetResult();
            Artifacts.PruneCommittedAsync().GetAwaiter().GetResult();
        }
        public RuntimeWorldChestCustodySnapshot Read()=>Artifacts.ReadManagedAsync(Load(File.ReadAllBytes(Path))).GetAwaiter().GetResult();
        public void Dispose(){owner.Dispose();System.IO.Directory.Delete(Directory,recursive:true);}
    }
}
