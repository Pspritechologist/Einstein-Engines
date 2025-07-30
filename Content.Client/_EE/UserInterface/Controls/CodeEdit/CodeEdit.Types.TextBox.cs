using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;
using Vector2 = System.Numerics.Vector2;

namespace Content.Client._EE.UserInterface.Controls.CodeEdit;

public sealed partial class CodeEdit
{
    internal sealed partial class RenderBox
    {
        internal sealed partial class TextBox
        {
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
    }
}
