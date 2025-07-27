namespace Content.Server._EE.IterasmTest;

[RegisterComponent]
public sealed partial class TestIterasmComponent : Component
{
    [DataField]
    public uint ExecutionsPerSecond = 1000;

    [DataField]
    public TimeSpan NextExecution = TimeSpan.Zero;

    [DataField]
    public int MaxQueueSize = 1000;

    public TestIterasmState IterasmState = default!;

    //TODO: Allow access to arbitrary fields??
    public Queue<(string Port, long Value)> IncomingQueue = new();
    // This queue exists because, although we *could* emit the signal immediately during execution,
    // allowing the iterasm lib to directly invoke a shit ton of game state like that seems super icky.
    // Would rather just handle any emitted signals during the update tick.
    public Queue<(string Port, long Value)> OutgoingQueue = new();
    public ushort RequestedRegister = 0;

    [ViewVariables(VVAccess.ReadOnly)]
    public ExecutionState CurrentState = ExecutionState.Halted;

    // Purely for debugging.
    [ViewVariables(VVAccess.ReadOnly)]
    public ulong Pc = 0;
}

public enum ExecutionState
{
    Halted,
    Running,
    WaitingForSignal,
    Busted,
}
