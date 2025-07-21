using Content.Server._EE.Iterasm.Binds;

namespace Content.Server._EE.Iterasm;

public abstract class IterasmState : IDisposable
{
    public IterasmVm? Vm { get; private set; }

    public void Compile(string src)
    {
        Vm?.Dispose();
        Vm = null;
        _customOps.Clear();
        Vm = IterasmVm.Compile(Utf8String.From(src), _opsCallback);
    }

    public State? State => Vm?.GetState() is { } state ? new State(state) : null;

    public virtual Func<State, long, bool>? CustomOps(string op) => null;

    public void Dispose()
    {
        Vm?.Dispose();
        Vm = null;
        _customOps.Clear();

        GC.SuppressFinalize(this); // Roslyn told me to add this :shrug:
    }

    // private readonly Utf8String _srcBuffer = Utf8String.Empty();
    private readonly CustomOpsCallback _opsCallback;
    private readonly List<OpCallback> _customOps = [];

    protected IterasmState() => _opsCallback = new CustomOpsCallback(op => CustomOpsCallback(op.String));

    private OptionOpCallback CustomOpsCallback(string op)
    {
        var customOp = CustomOps(op);
        if (customOp is null) return OptionOpCallback.None;

        var opDelegate = CreateOpDelegate(customOp, op);
        _customOps.Add(opDelegate);
        return OptionOpCallback.Some(opDelegate);
    }

    private static OpCallback CreateOpDelegate(Func<State, long, bool> customOp, string op) =>
        new((statePtr, args) => customOp(new State(statePtr), args) ? ResultUtf8String.Ok : ResultUtf8String.Err(Utf8String.From($"{op} failure :(")));
}

public readonly record struct State(IntPtr StatePtr)
{
    /// <summary> Increments the current project counter by 1. </summary>
    /// <returns>
    ///     Always returns <c><see langword="true"/></c>.
    ///     <br/>
    ///     This is so it can be the final `return` statement in an operation as a convenience.
    /// </returns>
    public bool Increment()
    {
        Interop.state_incr(StatePtr).AsOk();
        return true;
    }

    public ulong Pc { get => Interop.state_pc(StatePtr).AsOk(); set => Jump(value); }

    public void Jump(ulong addr) => Interop.state_jump_to(StatePtr, addr).AsOk();
    public void Jump(long offset) => Interop.state_jump(StatePtr, offset).AsOk();

    public void EnterFrame(ushort regCount, long data) => Interop.state_enter_frame(StatePtr, regCount, data).AsOk();
    public void EnterFrame(ushort regCount) => EnterFrame(regCount, 0);
    public long ExitFrame() => Interop.state_exit_frame(StatePtr).AsOk();

    public ulong FrameSize => Interop.state_frame_size(StatePtr).AsOk();
    public SliceU8 Stack => Interop.state_get_stack(StatePtr).AsOk();
    public void Set(ushort reg, long value) => Interop.state_set(StatePtr, reg, value).AsOk();
    public long Get(ushort reg) => Interop.state_get(StatePtr, reg).AsOk();
    public Frame? GetFrame(ulong frame) => Interop.state_get_frame(StatePtr, frame).AsOk().AsNullable();

    public ulong Alloc(nuint len) => Interop.state_alloc(StatePtr, len).AsOk();
    public void Free(ulong addr) => Interop.state_dealloc(StatePtr, addr).AsOk();
    public SliceMutU8 GetAddress(ulong addr, nuint len) => Interop.state_read(StatePtr, addr, len).AsOk();
}
