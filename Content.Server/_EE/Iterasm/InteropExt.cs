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
