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

public interface IOption<T>
{
    T AsNullable();
}

public partial struct OptionFrame : IOption<Frame?>
{
    public Frame? AsNullable() => IsSome ? AsSome() : null;
}
