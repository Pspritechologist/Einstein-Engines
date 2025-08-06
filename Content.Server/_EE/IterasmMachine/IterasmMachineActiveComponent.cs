using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._EE.IterasmMachine;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class IterasmMachineActiveComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextExecution = TimeSpan.Zero;

    [ViewVariables(VVAccess.ReadOnly)]
    public ulong Pc = 0;
}
