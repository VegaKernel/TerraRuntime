using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
namespace TerraRuntime.Application;
internal sealed record ClientNpcBuffRuntimeCommand(ConnectionHandle Connection, TerrariaNpcBuffState State) : RuntimeCommand;
