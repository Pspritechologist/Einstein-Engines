using System.Runtime.CompilerServices;

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
    public readonly IterasmState State
    {
        get
        {
            ObjectDisposedException.ThrowIf(_state == IntPtr.Zero, this);
            return new IterasmState(Interop.IterasmVm_get_state(_state).AsOkOrElse(static r => throw r.AsErr().Exception));
        }
    }

    public readonly void Compile(StrSlice src, CustomOpsCallback custom_ops, bool hold_state) =>
        NullC(state => Interop.IterasmVm_compile(state, src, custom_ops, hold_state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly void Compile(StrSlice src, CustomOpsCallbackDelegate custom_ops, bool hold_state) =>
        NullC(state => Interop.IterasmVm_compile(state, src, custom_ops, hold_state).AsOkOrElse(static r => throw r.AsErr().Exception));

    public readonly void RunToCompletion() => NullC(static state => Interop.IterasmVm_run_to_completion(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly bool RunStep() => NullC(static state => Interop.IterasmVm_run_step(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly bool RunSteps(ulong steps, out ulong stepsRun)
    {
        var res = NullC(state => Interop.IterasmVm_run_steps(state, steps).AsOkOrElse(static r => throw r.AsErr().Exception));
        stepsRun = res.steps_run;
        return res.done;
    }
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
                return new IterasmNotInitException();
            if (IsNoHeldState)
                return new IterasmNoStateHeldException();

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

#endregion
