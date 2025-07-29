namespace Content.Server._EE.Iterasm;

[RegisterComponent]
public sealed partial class IterasmDbgCartridgeComponent : Component
{
    /// <summary>
    /// The list of notes that got written down
    /// </summary>
    [DataField]
    public List<string> Notes = [];
}
