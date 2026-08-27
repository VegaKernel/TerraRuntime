using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

/// <summary>
/// Converts a frame into an owned command while the frame buffer is valid. Implementations must not
/// retain the frame, packet sequence or payload sequence after this method returns.
/// </summary>
public interface ITerrariaCommandDecoder<TCommand>
{
    TerrariaCommandDecodeResult TryDecode(in TerrariaFrame frame, out TCommand command);
}
