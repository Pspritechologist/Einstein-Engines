namespace Content.Server._EE.IterasmTest;

[RegisterComponent]
public sealed partial class TestIterasmComponent : Component
{
    [DataField]
    public uint ExecuteTime = 50;

    [DataField]
    public TimeSpan NextExecution = TimeSpan.Zero;

    public TestIterasmState IterasmState = default!;

    //TODO: Allow access to arbitrary fields??
    public Queue<(string Port, long Value)> IncomingQueue = new();
    public Queue<(string Port, long Value)> OutgoingQueue = new();
    public ushort RequestedRegister = 0;

    [ViewVariables(VVAccess.ReadOnly)]
    public ExecutionState CurrentState = ExecutionState.Idle;
}

public enum ExecutionState
{
    Idle,
    Running,
    WaitingForSignal,
}
