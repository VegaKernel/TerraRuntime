using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

/// <summary>
/// Consumes a frame while its underlying PipeReader buffer is still valid.
/// Implementations must not retain the frame or its sequences after this method returns.
/// </summary>
public interface ITerrariaFrameSink
{
    TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame);
}
