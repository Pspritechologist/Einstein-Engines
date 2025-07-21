using Content.Server._EE.Iterasm.Binds;

namespace Content.Server._EE.Iterasm;

public abstract class IterasmState : IDisposable
{
    // private readonly Utf8String _srcBuffer = Utf8String.Empty();
    protected readonly CustomOpsCallback OpsCallback;

    protected IterasmState()
    {
        OpsCallback = new CustomOpsCallback(op =>
        {
            if (CustomOps(op.String) is { } customOp)
                return OptionOpCallback.Some(new((statePtr, args) => customOp(new State(statePtr), args) ? ResultUtf8String.Ok : ResultUtf8String.Err(Utf8String.From("Failure :("))));
            return OptionOpCallback.None;
        });
    }

    public IterasmVm? Vm { get; private set; }

    public void Compile(string src) => Vm = IterasmVm.Compile(Utf8String.From(src), OpsCallback);

    public State? State => Vm?.GetState() is { } state ? new State(state) : null;

    public virtual Func<State, long, bool>? CustomOps(string op) => null;

    public void Dispose()
    {
        Vm?.Dispose();
        Vm = null;
    }
}

public readonly record struct State(IntPtr StatePtr)
{
    public bool Increment()
    {
        Interop.state_incr(StatePtr).AsOk();
        return true;
    }

    public void Set(ushort reg, long value) => Interop.state_set(StatePtr, reg, value).AsOk();
    public long Get(ushort reg) => Interop.state_get(StatePtr, reg).AsOk();

    public ulong Alloc(nuint len) => Interop.state_alloc(StatePtr, len).AsOk();
    public void Free(ulong addr) => Interop.state_dealloc(StatePtr, addr).AsOk();
    public SliceMutU8 GetAddress(ulong addr, nuint len) => Interop.state_read(StatePtr, addr, len).AsOk();
}
