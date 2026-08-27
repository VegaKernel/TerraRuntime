namespace TerraRuntime.Contracts.Runtime;

public enum GameLoopPhase : byte
{
    Ingress = 0,
    Commands = 1,
    Update = 2
}
