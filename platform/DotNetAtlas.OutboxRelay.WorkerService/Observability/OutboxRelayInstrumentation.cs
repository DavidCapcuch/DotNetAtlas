using System.Reflection;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability;

public static class OutboxRelayInstrumentation
{
    public static readonly AssemblyName AssemblyName = typeof(OutboxRelayInstrumentation).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();

    public static readonly string AppName = AssemblyName.Name!;
}
