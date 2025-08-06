using Content.Server._EE.IterasmMachine;

namespace Content.Server._EE.Iterasm;

[RegisterComponent]
public sealed partial class IterasmDbgCartridgeComponent : Component
{
    public Entity<IterasmMachineComponent>? ProgramTarget;
}
