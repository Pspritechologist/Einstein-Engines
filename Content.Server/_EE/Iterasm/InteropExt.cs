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
    public readonly VmState State => NullC(static state => new VmState(Interop.IterasmVm_get_state(state).AsOkOrElse(static r => throw r.AsErr().Exception)));

    public readonly void Compile(StrSlice src, CustomOpsCallback custom_ops, bool hold_state) =>
        NullC(state => Interop.IterasmVm_compile(state, src, custom_ops, hold_state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly void Compile(StrSlice src, CustomOpsCallbackDelegate custom_ops, bool hold_state) =>
        NullC(state => Interop.IterasmVm_compile(state, src, custom_ops, hold_state).AsOkOrElse(static r => throw r.AsErr().Exception));

    public readonly void RunToCompletion() => NullC(static state => Interop.IterasmVm_run_to_completion(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly void RunStep() => NullC(static state => Interop.IterasmVm_run_step(state).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly void RunSteps(nuint steps) => NullC(state => Interop.IterasmVm_run_steps(state, steps).AsOkOrElse(static r => throw r.AsErr().Exception));
    public readonly void Reset() => NullC(static state => Interop.IterasmVm_reset(state).AsOkOrElse(static r => throw r.AsErr().Exception));

    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(_state == IntPtr.Zero, this);
        Interop.IterasmVm_free(_state);
        _state = IntPtr.Zero;
    }
}

#region Error Handling

public sealed partial class Error
{
    public Exception Exception
    {
        get
        {
            if (IsPanic)
                return new(AsPanic().String);

            if (IsFailure)
                return new IterasmRuntimeErrorException(AsFailure().String);

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

public sealed class CompilationException(CompilationError e) : Exception()
{
    public CompilationErrorKind.CompilationErrorKindEnum Kind => e.kind;
    public nuint ErrorLine => e.line;
}

public sealed class IterasmRuntimeErrorException(string? msg) : Exception(msg)
{ }

#endregion
