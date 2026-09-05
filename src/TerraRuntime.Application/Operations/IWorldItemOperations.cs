namespace TerraRuntime.Application.Operations;

internal interface IWorldItemOperations
{
    RuntimeWorldItemsSnapshot CaptureSnapshot();
}
