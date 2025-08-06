namespace Content.Server._EE.IterasmMachine;

[RegisterComponent]
public sealed partial class IterasmMachineDefaultProgrammedComponent : Component
{
    /// <summary>
    ///     The program this machine should initialize with as Iterasm source code.<br/>
    ///     Invalid input is considered an error and will trigger a DebugAssert.
    /// </summary>
    [DataField(required: true)]
    public string Program;

    /// <summary>
    ///     Tracks whether this Component has already been used to program its relevant machine. <br/>
    ///     This ensures we don't end up overwriting something.
    /// </summary>
    public bool Spent = false;
}
