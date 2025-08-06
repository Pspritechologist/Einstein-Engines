using Content.Server._EE.Iterasm;

namespace Content.Server._EE.IterasmMachine;

[RegisterComponent]
public sealed partial class IterasmMachineComponent : Component, IDisposable
{
    [DataField]
    public TimeSpan ExecutionInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     If true, the machine will 'catch up' on operations, potentially performing
    ///     multiple in a single tick if it's running too slow.
    /// </summary>
    [DataField]
    public bool CatchupOps = true;

    public Dictionary<string, (string docs, Func<VmState, long, bool> op)> Ops = new();

    public IterasmMachineState State = default!;

    public void Dispose()
    {
        State?.Dispose();
        State = null!;
    }
}
