using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._EE.UserInterface.Controls.CodeEdit;

public sealed partial class CodeEdit
{
    /// <summary>
    ///     Interface for syntax highlighting and formatting of the text in a <see cref="CodeEdit"/>.
    /// </summary>
    public interface ICodeEditFormatter
    {
        /// <summary>
        ///     Called at the start of each draw frame to get the callback used throughout the draw.
        /// </summary>
        /// <param name="charIdx"> The first character that will be rendered during the draw. Will not always be the start of the text. </param>
        /// <param name="state"> The state used throughout the draw. </param>
        /// <returns>
        ///     The callback used to format the <see cref="CodeEdit"/>'s text throughout the frame.
        ///     <br/>
        ///     See the documentation of <see cref="TextFormattingDelegate"/> for more information on how to use this.
        /// </returns>
        TextFormattingDelegate GetFormatCallback(int charIdx, CodeEditFormatState state);

        /// <summary>
        ///     Gets called once for each character drawn in the <see cref="CodeEdit"/>.
        /// </summary>
        /// <remarks>
        ///     Note that characters not visible (i.e. outside of the active scroll area) are <i>not</i> drawn
        ///     and therefore will not be provided as indexes. It is thus necessary to have the required information
        ///     available ahead of time and not to rely on each character being passed to this method.
        /// </remarks>
        /// <param name="charIdx">
        ///     The index of the current character within the Rope.
        ///     <br/>
        ///     The Rune itself can be accessed with
        ///     <code>
        ///         if (Rope.Index(codeEdit.TextRope, charIdx) == '\n')
        ///             DoSomething();
        ///     </code>
        /// </param>
        /// <param name="state"> The formatting state, how you instruct the CodeEdit. See docs on <see cref="CodeEditFormatState"/> for more information.</param>
        delegate void TextFormattingDelegate(int charIdx, CodeEditFormatState fmtState);

        /// <summary>
        ///     Called any time the <see cref="CodeEdit"/>'s text is changed. Should be used to reset and clear any cached data.
        /// </summary>
        void ClearCache();
    }

    /// <summary>
    /// Sub-control responsible for doing the actual rendering work.
    /// </summary>
    /// <remarks>
    /// This is a sub-control to use <see cref="Control.RectClipContent"/>.
    /// </remarks>
    private sealed partial class RenderBox : BoxContainer
    {
        private readonly CodeEdit _master;

        public readonly LineNumberColumn LineNumberRenderColumn;
        public readonly TextBox TextRenderBox;

        public StyleBox? LineColumnPanel { set => LineNumberRenderColumn.PanelOverride = value; get => LineNumberRenderColumn.PanelOverride; }

        public RenderBox(CodeEdit master)
        {
            _master = master;

            RectClipContent = true;
            Orientation = LayoutOrientation.Horizontal;
            AddChild(LineNumberRenderColumn = new LineNumberColumn(this));
            AddChild(TextRenderBox = new TextBox(this));
        }

        internal sealed partial class TextBox : Control
        {
            private readonly CodeEdit _master;

            public TextBox(RenderBox master)
            {
                _master = master._master;
                HorizontalExpand = true;
                RectClipContent = true;
            }
        }

        internal sealed partial class LineNumberColumn : PanelContainer
        {
            private readonly CodeEdit _master;

            public LineNumberColumn(RenderBox master)
            {
                _master = master._master;
                SetWidth = (master._master.GetFont().GetCharMetrics(new('0'), UIScale)?.Width ?? 13) * 3;
                RectClipContent = true;
            }
        }
    }

    public sealed class CodeEditEventArgs(CodeEdit control, Rope.Node textRope) : EventArgs
    {
        public CodeEdit Control { get; } = control;
        public Rope.Node TextRope { get; } = textRope;
    }

    public sealed class CodeEditFormatState
    {
        public Font? OverrideFont { get; set; }
        public Color? TextColor { get; set; }
        public HighlightData? Highlight { get; set; }
        public UnderlineData? Underline { get; set; }

        public void Clear()
        {
            OverrideFont = null;
            TextColor = null;
            Highlight = null;
            Underline = null;
        }

        public record struct UnderlineData(Color Color, int Thickness = 1);
        public record struct HighlightData(Color Color, float HeightRatio = 1.0f);
    }

    /// <summary>
    /// Specifies which line the cursor is positioned at when on a word-wrapping break.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When words get pushed to a new line due to word-wrapping, a line break is tracked.
    /// For various reasons, people want to be able to place their cursor on both the end of the "top" line,
    /// as well as the start of the "bottom" line. These are however the same position in the source text,
    /// going by raw string indices at least. To allow the code to differentiate between these two positions,
    /// this bias value is tracked in all cursor positions.
    /// </para>
    /// <para>
    /// This is only for word-wrapping line breaks however. For explicit line breaks created with a newline character,
    /// the cursor bias should always be "top" so that everything works correctly.
    /// </para>
    /// </remarks>
    public enum LineBreakBias : byte
    {
        // @formatter:off
        Top = 0,
        Bottom = 1
        // @formatter:on
    }

    /// <summary>
    /// Stores the necessary data for a position in the cursor of the text.
    /// </summary>
    /// <param name="Index">The index of the cursor in the text contents.</param>
    /// <param name="Bias">Which direction to bias the cursor to </param>
    public record struct CursorPos(int Index, LineBreakBias Bias) : IComparable<CursorPos>
    {
        public static CursorPos Min(CursorPos a, CursorPos b)
        {
            var cmp = a.CompareTo(b);
            if (cmp < 0)
                return a;

            return b;
        }

        public static CursorPos Max(CursorPos a, CursorPos b)
        {
            var cmp = a.CompareTo(b);
            if (cmp > 0)
                return a;

            return b;
        }

        public int CompareTo(CursorPos other)
        {
            var indexComparison = Index.CompareTo(other.Index);
            if (indexComparison != 0)
                return indexComparison;

            // If two positions are at the same index, the one with bias top is considered earlier.
            return ((byte) Bias).CompareTo((byte) other.Bias);
        }

        public static bool operator <(CursorPos left, CursorPos right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator >(CursorPos left, CursorPos right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator <=(CursorPos left, CursorPos right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >=(CursorPos left, CursorPos right)
        {
            return left.CompareTo(right) >= 0;
        }
    }

    [Flags]
    internal enum MoveType
    {
        // @formatter:off
        Left = 1 << 0,
        Right = 1 << 1,
        LeftWord = 1 << 2,
        RightWord = 1 << 3,
        Up = 1 << 4,
        Down = 1 << 5,
        BeginOfLine = 1 << 6,
        EndOfLine = 1 << 7,

        ActionMask = (1 << 16) - 1,
        SelectFlag = 1 << 16,
        // @formatter:on
    }
}
