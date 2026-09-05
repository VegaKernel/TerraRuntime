namespace TerraRuntime.Application.Operations;

internal interface IPlayerOperations
{
    RuntimePlayersSnapshot CaptureSnapshot();
}
