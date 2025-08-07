namespace Content.Server._EE.IterasmMachine.Libraries;

[RegisterComponent]
public sealed partial class IterasmSignalLibComponent : Component
{
    [DataField]
    public int MaxQueueSize = 1000;

    [DataField]
    public string ValueKey = "value";

    public Queue<(string Port, long Value)> IncomingQueue = new();
}

[RegisterComponent]
public sealed partial class IterasmAwaitingSignalComponent : Component
{
    public ushort DstRegister;
}
