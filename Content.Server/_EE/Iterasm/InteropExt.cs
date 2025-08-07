using System.Runtime.CompilerServices;
using Content.Server._EE.Iterasm.Binds;

namespace Content.Server._EE.Iterasm.Binds;

public sealed partial class SliceMutU8
{
    public unsafe Span<byte> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        get => new(_data.ToPointer(), (int) _len);
    }
}

public partial struct StrSlice
{
    public StrSlice(byte[] data) => x0 = SliceU8.From(data);
    public StrSlice(IntPtr data, ulong len) => x0 = SliceU8.From(data, len);
    public StrSlice(SliceU8 data) => x0 = data;

    public readonly unsafe string String => System.Text.Encoding.UTF8.GetString(x0.ReadOnlySpan);
}

public partial struct IterasmVm() : IDisposable
{
    private IntPtr _state = Interop.new_vm();

    // Executes the given logic only if the state is not disposed.
    private readonly void NullC(Action<IntPtr> func)
    {
        ObjectDisposedException.ThrowIf(_state == IntPtr.Zero, this);
        func(_state);
    }
    private readonly T NullC<T>(Func<IntPtr, T> func)
    {
        ObjectDisposedException.ThrowIf(_state == IntPtr.Zero, this);
        return func(_state);
    }

    public readonly bool IsInit => NullC(static state => Interop.IterasmVm_is_init(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly bool HasHeldState => NullC(static state => Interop.IterasmVm_has_held_state(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    // Need C# 13 for ref structs to not be awful here :(
    public readonly VmState State
    {
        get
        {
            ObjectDisposedException.ThrowIf(_state == IntPtr.Zero, this);
            return new VmState(Interop.IterasmVm_get_state(_state).AsOkOrElse(static r => throw r.AsErr().Exception));
        }
    }

    public readonly void Compile(StrSlice src, CustomOpsCallback custom_ops, bool hold_state) =>
        NullC(state => Interop.IterasmVm_compile(state, src, custom_ops, hold_state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly void Compile(StrSlice src, CustomOpsCallbackDelegate custom_ops, bool hold_state) =>
        NullC(state => Interop.IterasmVm_compile(state, src, custom_ops, hold_state).AsOkOrElse(static r => throw r.AsErr().Exception));

    public readonly void RunToCompletion() => NullC(static state => Interop.IterasmVm_run_to_completion(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly bool RunStep() => NullC(static state => Interop.IterasmVm_run_step(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly bool RunSteps(nuint steps) => NullC(state => Interop.IterasmVm_run_steps(state, steps).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly void Reset() => NullC(static state => Interop.IterasmVm_reset(state).AsOkOrElse(static r => throw r.AsErr().Exception));

    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(_state == IntPtr.Zero, this);
        Interop.IterasmVm_free(_state);
        _state = IntPtr.Zero;
    }
}

#region Error Handling

public partial struct Error
{
    public Exception Exception
    {
        get
        {
            if (IsPanic)
                return new("Panic occurred in Iterasm library. THIS IS A BUG; Please report it.");

            if (IsRuntime)
                return AsRuntime().Exception;

            if (IsUninitializedVm)
                return new InvalidOperationException("Uninitialized VM state.");
            if (IsNoHeldState)
                return new InvalidOperationException("No held state.");

            if (IsCompilation)
                return new CompilationException(AsCompilation());

            throw new InteropException();
        }
    }
}

public partial struct RuntimeError
{
    public RuntimeError(IterasmRuntimeErrorException e)
    {
        line = e.Line;
        kind = e switch
        {
            IterasmRuntimeStackIndexOutOfBoundsException stackIndexOutOfBounds => RuntimeErrorKind.StackIndexOutOfBounds(new(stackIndexOutOfBounds.Index, stackIndexOutOfBounds.FrameSize)),
            IterasmRuntimeEntArgsUnderflowException entArgsUnderflow => RuntimeErrorKind.EntArgsUnderflow(new(entArgsUnderflow.Requested, entArgsUnderflow.Found)),
            IterasmRuntimeEntArgsOverflowException entArgsOverflow => RuntimeErrorKind.EntArgsOverflow(new(entArgsOverflow.Requested, entArgsOverflow.Space)),
            IterasmRuntimeExitFromEmptyFrameException => RuntimeErrorKind.ExitFromEmptyFrame,
            IterasmRuntimeExitArgsOverflowException exitArgsOverflow => RuntimeErrorKind.ExitArgsOverflow(new(exitArgsOverflow.Requested, exitArgsOverflow.Space)),
            IterasmRuntimeExitArgsUnderflowException exitArgsUnderflow => RuntimeErrorKind.ExitArgsUnderflow(new(exitArgsUnderflow.Requested, exitArgsUnderflow.Found)),
            IterasmRuntimeUnterminatedFormatStringException => RuntimeErrorKind.UnterminatedFormatString,
            IterasmRuntimeHeapAddrOutOfBoundsException heapAddrOutOfBounds => RuntimeErrorKind.HeapAddrOutOfBounds(new(heapAddrOutOfBounds.Addr, heapAddrOutOfBounds.Bounds)),
            IterasmRuntimeHeapRangeOutOfBoundsException heapRangeOutOfBounds => RuntimeErrorKind.HeapRangeOutOfBounds(new(heapRangeOutOfBounds.Addr, heapRangeOutOfBounds.Len, heapRangeOutOfBounds.Bounds)),
            IterasmRuntimeHeapAccessAfterFreeException heapAccessAfterFree => RuntimeErrorKind.HeapAccessAfterFree(new(heapAccessAfterFree.Addr)),
            IterasmRuntimeHeapAccessNullAddrException => RuntimeErrorKind.HeapAccessNullAddr,
            IterasmRuntimeHeapAllocOverflowException heapAllocOverflow => RuntimeErrorKind.HeapAllocOverflow(new(heapAllocOverflow.Size)),
            _ => throw new ArgumentOutOfRangeException(nameof(e), e, "Unknown runtime error type.")
        };
    }

    public Exception Exception
    {
        get
        {
            if (kind.AsProjectCounterOverflowOrNull() is { } projectCounterOverflow)
                return new IterasmRuntimeProjectCounterOverflowException(line, projectCounterOverflow.pc, projectCounterOverflow.bc);
            if (kind.AsStackIndexOutOfBoundsOrNull() is { } stackIndexOutOfBounds)
                return new IterasmRuntimeStackIndexOutOfBoundsException(line, stackIndexOutOfBounds.index, stackIndexOutOfBounds.frame_size);
            if (kind.AsEntArgsUnderflowOrNull() is { } entArgsUnderflow)
                return new IterasmRuntimeEntArgsUnderflowException(line, entArgsUnderflow.requested, entArgsUnderflow.found);
            if (kind.AsEntArgsOverflowOrNull() is { } entArgsOverflow)
                return new IterasmRuntimeEntArgsOverflowException(line, entArgsOverflow.requested, entArgsOverflow.space);
            if (kind.IsExitFromEmptyFrame)
                return new IterasmRuntimeExitFromEmptyFrameException(line);
            if (kind.AsExitArgsOverflowOrNull() is { } exitArgsOverflow)
                return new IterasmRuntimeExitArgsOverflowException(line, exitArgsOverflow.requested, exitArgsOverflow.space);
            if (kind.AsExitArgsUnderflowOrNull() is { } exitArgsUnderflow)
                return new IterasmRuntimeExitArgsUnderflowException(line, exitArgsUnderflow.requested, exitArgsUnderflow.found);
            if (kind.IsUnterminatedFormatString)
                return new IterasmRuntimeUnterminatedFormatStringException(line);
            if (kind.AsHeapAddrOutOfBoundsOrNull() is { } heapAddrOutOfBounds)
                return new IterasmRuntimeHeapAddrOutOfBoundsException(line, heapAddrOutOfBounds.addr, heapAddrOutOfBounds.bounds);
            if (kind.AsHeapRangeOutOfBoundsOrNull() is { } heapRangeOutOfBounds)
                return new IterasmRuntimeHeapRangeOutOfBoundsException(line, heapRangeOutOfBounds.addr, heapRangeOutOfBounds.len, heapRangeOutOfBounds.bounds);
            if (kind.AsHeapAccessAfterFreeOrNull() is { } heapAccessAfterFree)
                return new IterasmRuntimeHeapAccessAfterFreeException(line, heapAccessAfterFree.addr);
            if (kind.IsHeapAccessNullAddr)
                return new IterasmRuntimeHeapAccessNullAddrException(line);
            if (kind.AsHeapAllocOverflowOrNull() is { } heapAllocOverflow)
                return new IterasmRuntimeHeapAllocOverflowException(line, heapAllocOverflow.size);

            throw new InteropException();
        }
    }
}

public sealed class CompilationException(CompilationError e) : Exception()
{
    public CompilationErrorKind.CompilationErrorKindEnum Kind => e.kind;
    public nuint ErrorLine => e.line;
}

public abstract class IterasmRuntimeErrorException(nuint line) : Exception
{
    public nuint Line = line;
}

public sealed class IterasmRuntimeProjectCounterOverflowException(nuint line, nuint pc, nuint bc) : IterasmRuntimeErrorException(line)
{
    public nuint Pc = pc;
    public nuint Bc = bc;
}

public sealed class IterasmRuntimeStackIndexOutOfBoundsException(nuint line, ushort index, nuint frame_size) : IterasmRuntimeErrorException(line)
{
    public ushort Index = index;
    public nuint FrameSize = frame_size;
}

public sealed class IterasmRuntimeEntArgsUnderflowException(nuint line, ushort requested, ushort found) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Found = found;
}

public sealed class IterasmRuntimeEntArgsOverflowException(nuint line, ushort requested, ushort space) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Space = space;
}

public sealed class IterasmRuntimeExitFromEmptyFrameException(nuint line) : IterasmRuntimeErrorException(line)
{ }

public sealed class IterasmRuntimeExitArgsOverflowException(nuint line, ushort requested, ushort space) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Space = space;
}

public sealed class IterasmRuntimeExitArgsUnderflowException(nuint line, ushort requested, ushort found) : IterasmRuntimeErrorException(line)
{
    public ushort Requested = requested;
    public ushort Found = found;
}

public sealed class IterasmRuntimeUnterminatedFormatStringException(nuint line) : IterasmRuntimeErrorException(line)
{ }

public sealed class IterasmRuntimeHeapAddrOutOfBoundsException(nuint line, ulong addr, ulong bounds) : IterasmRuntimeErrorException(line)
{
    public ulong Addr = addr;
    public ulong Bounds = bounds;
}

public sealed class IterasmRuntimeHeapRangeOutOfBoundsException(nuint line, ulong addr, ulong len, ulong bounds) : IterasmRuntimeErrorException(line)
{
    public ulong Addr = addr;
    public ulong Len = len;
    public ulong Bounds = bounds;
}

public sealed class IterasmRuntimeHeapAccessAfterFreeException(nuint line, ulong addr) : IterasmRuntimeErrorException(line)
{
    public ulong Addr = addr;
}

public sealed class IterasmRuntimeHeapAccessNullAddrException(nuint line) : IterasmRuntimeErrorException(line)
{ }

public sealed class IterasmRuntimeHeapAllocOverflowException(nuint line, ulong size) : IterasmRuntimeErrorException(line)
{
    public ulong Size = size;
}

#endregion
