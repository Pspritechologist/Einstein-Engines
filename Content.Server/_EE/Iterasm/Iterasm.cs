using Content.Server._EE.Iterasm.Binds;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using MathNet.Numerics.Random;
using System.Buffers;

namespace Content.Server._EE.Iterasm;

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

    public VmState? State => Vm.IsInit ? Vm.State : null;

    public virtual Func<VmState, long, bool>? CustomOps(string op) => null;

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

        var opDelegate = CreateOpDelegate(customOp, op);
        _customOps.Add(opDelegate);
        return OptionOpCallback.Some(opDelegate);
    }

    private static OpCallback CreateOpDelegate(Func<VmState, long, bool> customOp, string op) =>
        new((statePtr, args) => customOp(new VmState(statePtr), args) ? ResultError.Ok : ResultError.Err(Error.Failure(Utf8String.From($"{op} failure :("))));
}

public readonly partial record struct VmState(IntPtr State)
{
    public ulong Pc { get => Interop.IterasmState_pc(State).AsOkOrElse(static r => throw r.AsErr().Exception); set => JumpTo(value); }

    public void Jump(long offset) => Interop.IterasmState_jump(State, offset).AsOkOrElse(static r => throw r.AsErr().Exception);
    public void JumpTo(ulong loc) => Interop.IterasmState_jump_to(State, loc).AsOkOrElse(static r => throw r.AsErr().Exception);
    public void Decrement() => Jump(-1);

    public ulong FrameSize => Interop.IterasmState_frame_size(State).AsOkOrElse(static r => throw r.AsErr().Exception);
    public SliceU8 Stack => Interop.IterasmState_get_stack(State).AsOkOrElse(static r => throw r.AsErr().Exception);

    public Frame? GetFrame(ulong index) => Interop.IterasmState_get_frame(State, index).AsOkOrElse(static r => throw r.AsErr().Exception).AsSomeOrNull();
    public void EnterFrame(ushort len, long store) => Interop.IterasmState_enter_frame(State, len, store).AsOkOrElse(static r => throw r.AsErr().Exception);
    public void EnterFrame(ushort regCount) => EnterFrame(regCount, 0);
    public long? ExitFrame() => Interop.IterasmState_exit_frame(State).AsOkOrElse(static r => throw r.AsErr().Exception).AsSomeOrNull();

    public long Get(ushort index) => Interop.IterasmState_get(State, index).AsOkOrElse(static r => throw r.AsErr().Exception);
    public void Set(ushort index, long value) => Interop.IterasmState_set(State, index, value).AsOkOrElse(static r => throw r.AsErr().Exception);

    public ulong Alloc(ulong len) => Interop.IterasmState_alloc(State, len).AsOkOrElse(static r => throw r.AsErr().Exception);
    public void Dealloc(ulong addr) => Interop.IterasmState_dealloc(State, addr).AsOkOrElse(static r => throw r.AsErr().Exception);
    public SliceMutU8 ReadAddr(ulong addr, nuint len) => Interop.IterasmState_read(State, addr, len).AsOkOrElse(static r => throw r.AsErr().Exception);
    public Chunk? GetAllocation(ulong index) => Interop.IterasmState_get_allocation(State, index).AsOkOrElse(static r => throw r.AsErr().Exception).AsSomeOrNull();

    public string? GetString(ushort reg) =>
        GetString((ulong) Get(reg), (nuint) Get((ushort) (reg + 1)));
    public string? GetString(ulong addr, nuint len) => addr == 0 || len == 0 ? null :
        System.Text.Encoding.UTF8.GetString(ReadAddr(addr, len).ReadOnlySpan);
}

public interface IIterasmTiming
{
    protected abstract IGameTiming Timing { get; }

    Func<VmState, long, bool>? TimingOps(string op) => op switch
    {
        "time" => GetTime,
        "timef" => GetTimeAsFloat,
        _ => null,
    };

    private bool GetTime(VmState state, long args)
    {
        var reg = (ushort) (args & 0xFFFF);
        var time = Timing.CurTime.TotalMilliseconds;
        state.Set(reg, (long) time);
        return true;
    }
    private bool GetTimeAsFloat(VmState state, long args)
    {
        var reg = (ushort) (args & 0xFFFF);
        var time = Timing.CurTime.TotalMilliseconds;
        state.Set(reg, BitConverter.DoubleToInt64Bits(time));
        return true;
    }
}

public interface IIterasmLogging
{
    protected virtual string? LogOp => "log";
    protected virtual string? LogOpI => "logi";

    Func<VmState, long, bool>? LoggingOps(string op)
    {
        if (op == LogOp)
            return (state, args) =>
            {
                var addr_reg = (ushort) (args & 0xFFFF);
                var len_reg = (ushort) ((args >> 16) & 0xFFFF);
                var addr = (ulong) state.Get(addr_reg);
                var len = (nuint) state.Get(len_reg);
                return LogCallback(state, state.GetString(addr, len));
            };
        if (op == LogOpI)
            return (state, args) =>
            {
                var addr = (ulong) (args & 0xFFFFFFFF);
                var len = (nuint) ((args >> 32) & 0xFFFFFFFF);
                return LogCallback(state, state.GetString(addr, len));
            };
        return null;
    }

    protected abstract bool LogCallback(VmState state, string? msg);
}

public interface IIterasmRNG
{
    protected abstract IRobustRandom Random { get; }

    Func<VmState, long, bool>? RNGOps(string op) => op switch
    {
        "frng" => RngFloat,
        "rng" => RngLong,
        _ => null,
    };

    private bool RngFloat(VmState state, long args)
    {
        var reg = (ushort) (args & 0xFFFF);
        var value = Random.NextDouble();
        state.Set(reg, BitConverter.DoubleToInt64Bits(value));
        return true;
    }

    private bool RngLong(VmState state, long args)
    {
        var reg = (ushort) (args & 0xFFFF);
        var value = Random.GetRandom().NextFullRangeInt64();
        state.Set(reg, value);
        return true;
    }
}
