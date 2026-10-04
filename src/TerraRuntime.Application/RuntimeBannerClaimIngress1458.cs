using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
namespace TerraRuntime.Application;

internal sealed record BannerClaimRuntimeCommand1458(ConnectionHandle Connection, TerrariaBannerClaim1458 Request) : RuntimeCommand;
internal interface IBannerClaimIngress1458 { bool TryPost(ConnectionHandle connection, in TerrariaBannerClaim1458 request); }
internal sealed class RuntimeBannerClaimIngress1458(IGameCommandIngress<RuntimeCommand> ingress) : IBannerClaimIngress1458
{
    public bool TryPost(ConnectionHandle connection, in TerrariaBannerClaim1458 request) => connection.IsAssigned &&
        (uint)request.Banner < TerrariaNpcDeathPreludeCodec1458.BannerCount &&
        ingress.TryPost(connection.Source, new BannerClaimRuntimeCommand1458(connection, request));
}

internal sealed class BannerClaimFrameSink1458(GameCommandSourceId source, PlayerBootstrapFrameSink bootstrap,
    ITerrariaFrameSink inner, IBannerClaimIngress1458 ingress) : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private bool malformed;
    public TerrariaFrameRejectionCategory RejectionCategory => malformed ? TerrariaFrameRejectionCategory.MalformedProtocol :
        inner is ITerrariaFrameRejectionSource nested ? nested.RejectionCategory : TerrariaFrameRejectionCategory.None;
    public TerrariaFrameSinkResult OnFrame(in TerraRuntime.Protocol.TerrariaFrame frame)
    {
        if (malformed) return TerrariaFrameSinkResult.Stop;
        if (!TerrariaNpcDeathPreludeCodec1458.IsBannerFrame(in frame)) return inner.OnFrame(in frame);
        if (!TerrariaNpcDeathPreludeCodec1458.TryDecodeClaim(in frame, out var request))
        { malformed = true; return TerrariaFrameSinkResult.Stop; }
        if (bootstrap.AssignedPlayerHandle is PlayerHandle player)
            ingress.TryPost(new ConnectionHandle(source, player), in request);
        return TerrariaFrameSinkResult.Continue;
    }
}
