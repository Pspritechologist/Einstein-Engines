using Content.Server._EE.Iterasm;
using Robust.Shared.Audio;

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

    [DataField]
    public SoundPathSpecifier? TickSound;

    [DataField]
    public SoundPathSpecifier? ErrorSound;

    public Dictionary<string, (string docs, IterasmOp op)> Ops = new();

    [Access(friends: typeof(IterasmMachineSystem))]
    public IterasmMachineState Iterasm = default!;

    public void Dispose()
    {
        Iterasm?.Dispose();
        Iterasm = null!;
    }
}
