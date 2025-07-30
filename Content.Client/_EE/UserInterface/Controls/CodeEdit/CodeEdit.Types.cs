using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._EE.UserInterface.Controls.CodeEdit;

public sealed partial class CodeEdit
{
    /// <summary>
    /// Sub-control responsible for doing the actual rendering work.
    /// </summary>
    /// <remarks>
    /// This is a sub-control to use <see cref="Control.RectClipContent"/>.
    /// </remarks>
    internal sealed partial class RenderBox : BoxContainer
    {
        private readonly CodeEdit _master;

        private readonly LineNumberColumn _lineNumberColumn;
        private readonly TextBox _textBox;

        public StyleBox? LineColumnPanel { set => _lineNumberColumn.PanelOverride = value; get => _lineNumberColumn.PanelOverride; }

        public RenderBox(CodeEdit master)
        {
            _master = master;

            RectClipContent = true;
            Orientation = LayoutOrientation.Horizontal;
            AddChild(_lineNumberColumn = new LineNumberColumn(this));
            AddChild(_textBox = new TextBox(this));
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
