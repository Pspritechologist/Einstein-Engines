using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;
using Vector2 = System.Numerics.Vector2;

namespace Content.Client._EE.UserInterface.Controls.CodeEdit;

public sealed partial class CodeEdit
{
    private sealed partial class RenderBox
    {
        internal sealed partial class LineNumberColumn
        {
            protected override void Draw(DrawingHandleScreen handle)
            {
                // Draw line numbers with scrolling adjustment
                var lineNumberFont = _master.GetFont();
                var lineNumberColor = Color.Gray;
                var lineNumberBaseLine = new Vector2(5, -_master._scrollBar.Value + lineNumberFont.GetAscent(UIScale));

                void DrawNum(uint num, Vector2 pos)
                {
                    foreach (var r in num.ToString().EnumerateRunes())
                        pos.X += lineNumberFont.DrawChar(handle, r, pos, UIScale, lineNumberColor);
                }

                var lineNumber = 1u;

                var inWordWrap = false;

                foreach (var breakIdx in _master._lineBreaks)
                {
                    // Only increment the line count if there was an explicit break; Word wrapping doesn't count.
                    var lineBreak = Rope.Index(_master.TextRope, breakIdx) == '\n';
                    // A num is drawn if there's a break, or if we're only *starting* a wrap, at the beginning.
                    var drawNum = (lineBreak || !inWordWrap) && !inWordWrap;

                    // Draw the number as long as we're not in a wrap.
                    // We use a copy of the base line so we don't modify the original for next line.
                    if (drawNum)
                    {
                        DrawNum(lineNumber, lineNumberBaseLine);
                        lineNumber++; // Only increment if we drew a number.
                    }

                    // Move the cursor to the next line.
                    lineNumberBaseLine.Y += _master.GetFont().GetLineHeight(UIScale);

                    // In the case of a word wrap, make note we're currently in one so we know not to draw a line number until we're out.
                    inWordWrap = !lineBreak;
                }

                // There might be one more line number to draw right at the end.
                if (!inWordWrap)
                    DrawNum(lineNumber, lineNumberBaseLine);
            }
        }
    }
}
