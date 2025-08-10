using Content.Server._EE.Iterasm.Binds;
using System.Buffers;

namespace Content.Server._EE.Iterasm;

public delegate bool IterasmOp(IterasmState state, long args);

public abstract class Iterasm : IDisposable
{
    protected IterasmVm Vm { get; set; }

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

    public bool TryGetState(out IterasmState state)
    {
        state = default;
        if (!Vm.IsInit)
            return false;

        state = Vm.State;
        return true;
    }

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

    protected Iterasm()
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
            return ResultBoolRuntimeErrorKind.Ok(customOp(new IterasmState(statePtr), args));
        }
        catch (IterasmRuntimeErrorException e)
        {
            return ResultBoolRuntimeErrorKind.Err(new RuntimeError(e).kind);
        }
    });
}

public readonly ref struct IterasmState(IntPtr state)
{
    public readonly ulong Pc
    {
        get
        {
            try { return Interop.IterasmState_pc(state).AsOkOrElse(static r => throw r.AsErr().Exception); }
            catch (IterasmInvalidOperationException) { return 0; } // Low priority error case.
        }
    }

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

#region Exceptions

public sealed class CompilationException(CompilationError e) : Exception()
{
    public CompilationErrorKind.CompilationErrorKindEnum Kind => e.kind;
    public nuint ErrorLine => e.line;
}

public abstract class IterasmInvalidOperationException(string msg) : Exception(msg)
{ }

public sealed class IterasmNotInitException() : IterasmInvalidOperationException("Iterasm VM is not initialized.")
{ }

public sealed class IterasmNoStateHeldException() : IterasmInvalidOperationException("Attempted to reset the state of an Iterasm VM that does not hold any state.")
{ }

public abstract class IterasmRuntimeErrorException(nuint line) : Exception
{
    public nuint Line = line;
}

public sealed class IterasmRuntimeProjectCounterOverflowException(nuint line, nuint pc, nuint bc) : IterasmRuntimeErrorException(line)
{
    public nuint Pc = pc;
    public nuint Bc = bc;

    public override string Message => $"Program counter overflow at line {Line}: {Pc} > {Bc}.";
}

public sealed class IterasmRuntimeStackIndexOutOfBoundsException(nuint line, ushort index, nuint frame_size) : IterasmRuntimeErrorException(line)
{
    public ushort Index = index;
    public nuint FrameSize = frame_size;

    public override string Message => $"Stack index out of bounds at line {Line}: {Index} >= {FrameSize}.";
}

public sealed class IterasmRuntimeEntArgsUnderflowException(nuint line, ushort requested, ushort found) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Found = found;

    public override string Message => $"Underflow in ENT arguments at line {Line}: requested {Requested}, found {Found}.";
}

public sealed class IterasmRuntimeEntArgsOverflowException(nuint line, ushort requested, ushort space) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Space = space;

    public override string Message => $"Overflow in ENT arguments at line {Line}: requested {Requested}, space for {Space}.";
}

public sealed class IterasmRuntimeExitFromEmptyFrameException(nuint line) : IterasmRuntimeErrorException(line)
{
    public override string Message => $"Exit from empty frame at line {Line}.";
}

public sealed class IterasmRuntimeExitArgsOverflowException(nuint line, ushort requested, ushort space) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Space = space;

    public override string Message => $"Overflow in EXIT arguments at line {Line}: requested {Requested}, space for {Space}.";
}

public sealed class IterasmRuntimeExitArgsUnderflowException(nuint line, ushort requested, ushort found) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Found = found;

    public override string Message => $"Underflow in EXIT arguments at line {Line}: requested {Requested}, found {Found}.";
}

public sealed class IterasmRuntimeUnterminatedFormatStringException(nuint line) : IterasmRuntimeErrorException(line)
{
    public override string Message => $"Unterminated format string at line {Line}.";
}

public sealed class IterasmRuntimeHeapAddrOutOfBoundsException(nuint line, ulong addr, ulong bounds) : IterasmRuntimeErrorException(line)
{
    public ulong Addr = addr;
    public ulong Bounds = bounds;

    public override string Message => $"MEM address out of bounds at line {Line}: {Addr} >= {Bounds}.";
}

public sealed class IterasmRuntimeHeapRangeOutOfBoundsException(nuint line, ulong addr, ulong len, ulong bounds) : IterasmRuntimeErrorException(line)
{
    public ulong Addr = addr;
    public ulong Len = len;
    public ulong Bounds = bounds;

    public override string Message => $"MEM range out of bounds at line {Line}: {Addr} + {Len} > {Bounds}.";
}

public sealed class IterasmRuntimeHeapAccessAfterFreeException(nuint line, ulong addr) : IterasmRuntimeErrorException(line)
{
    public ulong Addr = addr;

    public override string Message => $"MEM access after free at line {Line}: {Addr}.";
}

public sealed class IterasmRuntimeHeapAccessNullAddrException(nuint line) : IterasmRuntimeErrorException(line)
{
    public override string Message => $"MEM access with NIL address at line {Line}.";
}

public sealed class IterasmRuntimeHeapAllocOverflowException(nuint line, ulong size) : IterasmRuntimeErrorException(line)
{
    public ulong Size = size;

    public override string Message => $"MEM allocation overflow at line {Line}: {Size} bytes.";
}

//TODO Iterasm: Potentially allow defining custom runtime errors...

#endregion
