namespace Content.Server._EE.IterasmMachine.Libraries;

[RegisterComponent]
public sealed partial class IterasmSleepLibComponent : Component
{ }

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class IterasmSleepingComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), AutoPausedField]
    public TimeSpan WakeupTime = TimeSpan.Zero;
}
