using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;
using Vector2 = System.Numerics.Vector2;

namespace Content.Client._EE.UserInterface.Controls.CodeEdit;

public sealed partial class CodeEdit
{
    /// <summary>
    /// Sub-control responsible for doing the actual rendering work.
    /// </summary>
    /// <remarks>
    /// This is a sub-control to use <see cref="Control.RectClipContent"/>.
    /// </remarks>
    internal sealed class RenderBox : Control
    {
        private readonly CodeEdit _master;

        public RenderBox(CodeEdit master)
        {
            _master = master;

            RectClipContent = true;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var drawBox = PixelSizeBox;
            var font = _master.GetFont();
            var renderedTextColor = _master.GetFontColor();

            var scrollOffset = -_master._scrollBar.Value;

            var scale = UIScale;
            var baseLine = new Vector2(0, scrollOffset + font.GetAscent(scale));
            var height = font.GetLineHeight(scale);
            var descent = font.GetDescent(scale);

            var viewT = -scrollOffset;

            var startLineIndex = (int) (viewT / height);
            var startIdx = _master.GetStartOfLine(startLineIndex);

            var lineBreakIndex = startLineIndex;
            var count = startIdx;

            var selectionLower = _master.SelectionLower;
            var selectionUpper = _master.SelectionUpper;

            baseLine.Y += startLineIndex * height;

            int? selectStartPos = null;
            int? selectEndPos = null;
            var selecting = false;

            var imeStartIndex = -1;
            var imeEndIndex = -1;

            int? imeStartPos = null;
            int? imeEndPos = null;
            var imeing = false;

            if (_master._imeData.HasValue)
            {
                var (start, length) = _master._imeData.Value;
                imeStartIndex = start.Index;
                imeEndIndex = imeStartIndex + length;

                if (imeStartIndex < startIdx && imeEndIndex > startIdx)
                {
                    imeing = true;
                    imeStartPos = 0;
                }
            }

            if (selectionLower.Index < startIdx && selectionUpper.Index > startIdx)
            {
                selecting = true;
                selectStartPos = 0;
            }

            foreach (var rune in Rope.EnumerateRunes(_master.GetDisplayRope(), startIdx))
            {
                CheckDrawCursors(LineBreakBias.Top);

                if (lineBreakIndex < _master._lineBreaks.Count
                    && _master._lineBreaks[lineBreakIndex] == count)
                {
                    // Line break
                    // Check to handle

                    PostDrawLine();

                    baseLine = new Vector2(drawBox.Left, baseLine.Y + height);
                    lineBreakIndex += 1;

                    selectStartPos = selecting ? 0 : null;
                    selectEndPos = null;

                    imeStartPos = imeing ? 0 : null;
                    imeEndPos = null;

                    if (baseLine.Y - height > drawBox.Height)
                    {
                        // Past the bottom of the visible area of the screen: no need to render anything else.
                        break;
                    }
                }

                CheckDrawCursors(LineBreakBias.Bottom);

                baseLine.X += font.DrawChar(handle, rune, baseLine, scale, renderedTextColor);

                count += rune.Utf16SequenceLength;
            }

            // Also draw cursor if it's at the very end.
            CheckDrawCursors(LineBreakBias.Bottom);
            CheckDrawCursors(LineBreakBias.Top);
            PostDrawLine();

            void CheckDrawCursors(LineBreakBias bias)
            {
                var pos = new CursorPos(count, bias);

                if (_master.HasKeyboardFocus() && _master._cursorPosition == pos)
                {
                    var cursorColor = _master.StylePropertyDefault(
                        StylePropertyCursorColor,
                        Color.White);

                    cursorColor.A *= _master._blink.Opacity;

                    handle.DrawRect(
                        new UIBox2(
                            baseLine.X,
                            baseLine.Y - height + descent,
                            baseLine.X + 1,
                            baseLine.Y + descent),
                        cursorColor);

                    if (UserInterfaceManager.KeyboardFocused == _master && Root?.Window is { } window)
                    {
                        var box = (UIBox2i) new UIBox2(
                            drawBox.Left,
                            baseLine.Y - height + descent,
                            drawBox.Right,
                            baseLine.Y + descent);
                        var cursorOffset = baseLine.X - drawBox.Left;

                        window.TextInputSetRect(box.Translated(GlobalPixelPosition), (int) cursorOffset);
                    }
                }

                if (selectionLower == pos)
                {
                    selecting = true;
                    selectStartPos = (int) baseLine.X;
                }

                if (selectionUpper == pos)
                {
                    selecting = false;
                    selectEndPos = (int) baseLine.X;
                }

                if (count == imeStartIndex)
                {
                    imeing = true;
                    imeStartPos = (int) baseLine.X;
                }

                if (count == imeEndIndex)
                {
                    imeing = false;
                    imeEndPos = (int) baseLine.X;
                }
            }

            void PostDrawLine()
            {
                if (selectStartPos != null)
                {
                    var rect = new UIBox2(
                        selectStartPos.Value,
                        baseLine.Y - height + descent,
                        selectEndPos ?? baseLine.X,
                        baseLine.Y + descent
                    );

                    var color = _master.StylePropertyDefault(
                        StylePropertySelectionColor,
                        Color.CornflowerBlue.WithAlpha(0.25f));

                    handle.DrawRect(rect, color);
                }

                if (_master._imeData.HasValue && imeStartPos.HasValue)
                {
                    // Draw IME underline.
                    var y = baseLine.Y + font.GetDescent(scale);
                    var rect = new UIBox2(
                        imeStartPos.Value,
                        y - 1,
                        imeEndPos ?? baseLine.X,
                        y
                    );

                    handle.DrawRect(rect, renderedTextColor);
                }
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
