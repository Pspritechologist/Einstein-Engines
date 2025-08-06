namespace Content.Server._EE.IterasmMachine.Libraries;

[RegisterComponent]
public sealed partial class IterasmLogLibComponent : Component
{
    /// <summary>
    ///     If provided, any logged messages will be formatted using this string.<br/>
    ///     The following patterns will be substituted:
    ///     <list type="bullet">
    ///         <item>
    ///             <term> {MSG} </term>
    ///             <description> The logged message itself. </description>
    ///         </item>
    ///         <item>
    ///             <term> {DEV} </term>
    ///             <description>
    ///                 The 'device number' for the machine.
    ///                 This is the EntityUid and is used in various places for aesthetic purposes.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <term> {INSERT} </term>
    ///             <description> The value of <see cref="FormatInsert"/>. </description>
    ///         </item>
    ///         <item>
    ///             <term> {TIME} </term>
    ///             <description> The current in-game time. </description>
    ///         </item>
    ///     </list>
    /// </summary>
    [DataField]
    public string? FormatString = null;

    /// <summary>
    ///     Can be used to insert something into the <see cref="FormatString"/> without
    ///     needing to modify the format string itself.
    /// </summary>
    [DataField]
    public string? FormatInsert = null;

    /// <summary>
    ///     Messages are stored here before being logged on the next tick.
    /// </summary>
    public List<string> LogQueue = new();
}

public readonly record struct IterasmMachineLogEvent(string Message);
