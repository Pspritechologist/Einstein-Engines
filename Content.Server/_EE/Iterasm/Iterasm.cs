using Content.Server._EE.Iterasm.Binds;
using System.Buffers;

namespace Content.Server._EE.Iterasm;

public delegate bool IterasmOp(VmState state, long args);

public abstract class IterasmState : IDisposable
{
    public IterasmVm Vm { get; private set; }

    public unsafe void Compile(string src)
    {
        var len = System.Text.Encoding.UTF8.GetByteCount(src);
        var buf = ArrayPool<byte>.Shared.Rent(len);

        try
        {
            var encoded = System.Text.Encoding.UTF8.GetBytes(src.AsSpan(), buf.AsSpan());
            if (encoded != len)
                throw new InvalidOperationException($"Failed to encode string to UTF-8. Expected {len} bytes, got {encoded} bytes.");

            fixed (byte* ptr = buf)
                Compile(new StrSlice((nint) ptr, (ulong) encoded));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    public void Compile(StrSlice src, bool holdState = true)
    {
        _customOps.Clear();
        Vm.Compile(src, _opsCallback, holdState);
    }

    public VmState State => Vm.State;

    public virtual IterasmOp? CustomOps(string op) => null;

    public void Dispose()
    {
        Vm.Dispose();
        _opsCallback.Dispose();
        foreach (var cb in _customOps)
            cb.Dispose();
        _customOps.Clear();
        GC.SuppressFinalize(this); // Roslyn told me to add this :shrug:
    }

    private readonly CustomOpsCallback _opsCallback;
    private readonly List<OpCallback> _customOps = [];

    protected IterasmState()
    {
        Vm = new IterasmVm();
        _opsCallback = new CustomOpsCallback(op => CustomOpsCallback(op.String));
    }

    private OptionOpCallback CustomOpsCallback(string op)
    {
        var customOp = CustomOps(op);
        if (customOp is null) return OptionOpCallback.None;

        var opDelegate = CreateOpDelegate(customOp);
        _customOps.Add(opDelegate);
        return OptionOpCallback.Some(opDelegate);
    }

    private static OpCallback CreateOpDelegate(IterasmOp customOp) => new((statePtr, args) =>
    {
        try
        {
            return ResultBoolRuntimeErrorKind.Ok(customOp(new VmState(statePtr), args));
        }
        catch (IterasmRuntimeErrorException e)
        {
            return ResultBoolRuntimeErrorKind.Err(new RuntimeError(e).kind);
        }
    });
}

public readonly ref partial struct VmState(IntPtr state)
{
    public readonly ulong Pc { get => Interop.IterasmState_pc(state).AsOkOrElse(static r => throw r.AsErr().Exception); set => JumpTo(value); }

    public readonly void Jump(long offset) => Interop.IterasmState_jump(state, offset).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly void JumpTo(ulong loc) => Interop.IterasmState_jump_to(state, loc).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly void Decrement() => Jump(-1);

    public readonly ulong FrameSize => Interop.IterasmState_frame_size(state).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly SliceU8 Stack => Interop.IterasmState_get_stack(state).AsOkOrElse(static r => throw r.AsErr().Exception);

    public readonly Frame? GetFrame(ulong index) => Interop.IterasmState_get_frame(state, index).AsOkOrElse(static r => throw r.AsErr().Exception).AsSomeOrNull();
    public readonly void EnterFrame(ushort len, long store) => Interop.IterasmState_enter_frame(state, len, store).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly void EnterFrame(ushort regCount) => EnterFrame(regCount, 0);
    public readonly long? ExitFrame() => Interop.IterasmState_exit_frame(state).AsOkOrElse(static r => throw r.AsErr().Exception).AsSomeOrNull();

    public readonly long Get(ushort index) => Interop.IterasmState_get(state, index).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly void Set(ushort index, long value) => Interop.IterasmState_set(state, index, value).AsOkOrElse(static r => throw r.AsErr().Exception);

    public readonly ulong Alloc(ulong len) => Interop.IterasmState_alloc(state, len).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly void Dealloc(ulong addr) => Interop.IterasmState_dealloc(state, addr).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly SliceMutU8 ReadAddr(ulong addr, nuint len) => Interop.IterasmState_read(state, addr, len).AsOkOrElse(static r => throw r.AsErr().Exception);
    public readonly Chunk? GetAllocation(ulong index) => Interop.IterasmState_get_allocation(state, index).AsOkOrElse(static r => throw r.AsErr().Exception).AsSomeOrNull();

    public readonly string? GetString(ushort reg) =>
        GetString((ulong) Get(reg), (nuint) Get((ushort) (reg + 1)));
    public readonly string? GetString(ulong addr, nuint len) => addr == 0 || len == 0 ? null :
        System.Text.Encoding.UTF8.GetString(ReadAddr(addr, len).ReadOnlySpan);
}
