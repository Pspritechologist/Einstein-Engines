using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Collections;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Vector2 = System.Numerics.Vector2;

namespace Content.Client._EE.UserInterface.Controls.CodeEdit;

public sealed partial class CodeEdit : Control
{
    [Dependency] private readonly IClipboardManager _clipboard = null!;

    // @formatter:off
    public const string StylePropertyCursorColor = "cursor-color";
    public const string StylePropertySelectionColor = "selection-color";
    public const string StylePseudoClassNotEditable = "notEditable";
    public const string StylePseudoClassPlaceholder = "placeholder";
    // @formatter:on

    private readonly RenderBox _renderBox;
    private readonly VScrollBar _scrollBar;

    private CursorPos _cursorPosition;
    private CursorPos _selectionStart;

    // Cached last horizontal cursor position for vertical cursor movement.
    // Conceptually, moving the cursor up/down across "keeps" the horizontal position as much as possible.
    // This allows it to remain in position even when the cursor is moving past "valleys" of short lines.
    private float? _horizontalCursorPos;

    // Stores the positions of all line breaks inside the edited text.
    // The format is a list of all indices into the text rope where a line break should occur.
    // These line breaks are "before" the indexed character. So if I have the string "AB", with a line break at index 1,
    // that means the line break is BETWEEN A and B.
    //
    // Line breaks come from either explicit newlines (\n) or from word-wrapping behavior.
    // It should be noted that in the case of newlines, the newline character is actually considered "on the next line".
    // This also has implications for cursor bias, see below.
    private ValueList<int> _lineBreaks;

    private Rope.Node _textRope = Rope.Leaf.Empty;
    private Rope.Node? _placeholder;
    private bool _lineUpdateQueued;

    private bool _editable = true;

    // Uncommitted IME positions are stored directly in the text rope.
    // This field tracks the start position thereof, and how long it is.
    // The intent is that the text is cut from the rope again if the composition gets cancelled or edited.
    private (CursorPos start, int length)? _imeData;

    // Yay fancy blink animation!!!!
    private CodeEditShared.CursorBlink _blink;

    // State for click-hold text selection.
    private bool _mouseSelectingText;
    private Vector2 _lastMouseSelectPos;

    // Debug overlay stuff.
    internal bool DebugOverlay;
    private Vector2? _lastDebugMousePos;

    public event Action<CodeEditEventArgs>? OnTextChanged;

    public CodeEdit()
    {
        IoCManager.InjectDependencies(this);

        AddChild(_renderBox = new RenderBox(this));
        AddChild(_scrollBar = new VScrollBar { HorizontalAlignment = HAlignment.Right });

        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.IBeam;
    }

    /// <summary>
    /// The current position of the cursor in the text rope.
    /// </summary>
    /// <remarks>
    /// See the class remarks for how text selection works in this control.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown if the given position would put the cursor inside a surrogate pair.
    /// </exception>
    public CursorPos CursorPosition
    {
        get => _cursorPosition;
        set
        {
            var clamped = MathHelper.Clamp(value.Index, 0, TextLength);
            if (TextLength != 0 && TextLength != clamped && !Rope.TryGetRuneAt(TextRope, clamped, out _))
                throw new ArgumentException("Cannot set cursor inside surrogate pair.");

            _cursorPosition = value with { Index = clamped };
            _selectionStart = _cursorPosition;
        }
    }

    /// <summary>
    /// The start position of the selection in the text rope.
    /// </summary>
    /// <remarks>
    /// See the class remarks for how text selection works in this control.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown if the given position would put the cursor inside a surrogate pair.
    /// </exception>
    public CursorPos SelectionStart
    {
        get => _selectionStart;
        set
        {
            var clamped = MathHelper.Clamp(value.Index, 0, TextLength);
            if (TextLength != 0 && TextLength != clamped && !Rope.TryGetRuneAt(TextRope, clamped, out _))
                throw new ArgumentException("Cannot set cursor inside surrogate pair.");

            _selectionStart = value with { Index = clamped };
        }
    }

    /// <summary>
    /// The text rope that can be viewed and edited.
    /// </summary>
    public Rope.Node TextRope
    {
        get => _textRope;
        set
        {
            _textRope = value;
            QueueLineBreakUpdate();
            UpdatePseudoClass();
        }
    }

    /// <summary>
    /// True to allow editing by the user. False to make it read-only.
    /// </summary>
    public bool Editable
    {
        get => _editable;
        set
        {
            _editable = value;
            DefaultCursorShape = _editable ? CursorShape.IBeam : CursorShape.Arrow;
            UpdatePseudoClass();
        }
    }

    /// <summary>
    /// The lower (in string index terms) end of the active text selection.
    /// </summary>
    /// <remarks>
    /// Confusingly, because text is read top-to-bottom, "lower" is actually higher up on your monitor.
    /// </remarks>
    public CursorPos SelectionLower => CursorPos.Min(_selectionStart, _cursorPosition);

    /// <summary>
    /// The upper (in string index terms) end of the active text selection.
    /// </summary>
    /// <remarks>
    /// Confusingly, because text is read top-to-bottom, "upper" is actually lower down on your monitor.
    /// </remarks>
    public CursorPos SelectionUpper => CursorPos.Max(_selectionStart, _cursorPosition);

    /// <summary>
    /// The amount of chars selected by the active selection.
    /// </summary>
    public int SelectionLength => Math.Abs(_selectionStart.Index - _cursorPosition.Index);

    // TODO: cache
    public int TextLength => (int) Rope.CalcTotalLength(TextRope);

    public System.Range SelectionRange => (SelectionLower.Index)..(SelectionUpper.Index);

    /// <summary>
    /// A placeholder text to be displayed when no actual text is entered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This isn't editable by the user, but to make the internals simpler, it is still exposed as a rope.
    /// </para>
    /// <para>
    /// When set to null, the placeholder style pseudo-class will not be applied when the actual text content is empty.
    /// If set to an empty text rope this does happen, but obviously no text is displayed either way.
    /// </para>
    /// </remarks>
    [ViewVariables(VVAccess.ReadWrite)]
    public Rope.Node? Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
            UpdatePseudoClass();
            QueueLineBreakUpdate();
        }
    }

    private bool IsPlaceholderVisible => Rope.IsNullOrEmpty(_textRope) && _placeholder != null;

    // Table used by cursor movement system, see below.
    private static readonly FrozenDictionary<BoundKeyFunction, MoveType> MoveTypeMap = new Dictionary<BoundKeyFunction, MoveType>()
    {
        // @formatter:off
        { EngineKeyFunctions.TextCursorLeft,            MoveType.Left        },
        { EngineKeyFunctions.TextCursorRight,           MoveType.Right       },
        { EngineKeyFunctions.TextCursorUp,              MoveType.Up          },
        { EngineKeyFunctions.TextCursorDown,            MoveType.Down        },
        { EngineKeyFunctions.TextCursorWordLeft,        MoveType.LeftWord    },
        { EngineKeyFunctions.TextCursorWordRight,       MoveType.RightWord   },
        { EngineKeyFunctions.TextCursorBegin,           MoveType.BeginOfLine },
        { EngineKeyFunctions.TextCursorEnd,             MoveType.EndOfLine   },

        { EngineKeyFunctions.TextCursorSelectLeft,      MoveType.Left        | MoveType.SelectFlag },
        { EngineKeyFunctions.TextCursorSelectRight,     MoveType.Right       | MoveType.SelectFlag },
        { EngineKeyFunctions.TextCursorSelectUp,        MoveType.Up          | MoveType.SelectFlag },
        { EngineKeyFunctions.TextCursorSelectDown,      MoveType.Down        | MoveType.SelectFlag },
        { EngineKeyFunctions.TextCursorSelectWordLeft,  MoveType.LeftWord    | MoveType.SelectFlag },
        { EngineKeyFunctions.TextCursorSelectWordRight, MoveType.RightWord   | MoveType.SelectFlag },
        { EngineKeyFunctions.TextCursorSelectBegin,     MoveType.BeginOfLine | MoveType.SelectFlag },
        { EngineKeyFunctions.TextCursorSelectEnd,       MoveType.EndOfLine   | MoveType.SelectFlag },
        // @formatter:on
    }.ToFrozenDictionary();

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Handled)
            return;

        var doCursorVisible = true;

        if (MoveTypeMap.TryGetValue(args.Function, out var moveType))
        {
            // Most movement operations like normal vs word-bound work the same with or without an active text selection.
            // To allow sharing this code, we map key functions to a flag that defines the actual operation,
            // making code reuse easy.

            // Calculate actual new position.
            var selectFlag = (moveType & MoveType.SelectFlag) != 0;
            var newPos = CalcCursorMove(moveType & MoveType.ActionMask, selectFlag, out var keepH);

            _cursorPosition = newPos;

            // If not selecting text, keep selection start at cursor (possibly breaking an active selection).
            if (!selectFlag)
                _selectionStart = _cursorPosition;

            if (!keepH)
                InvalidateHorizontalCursorPos();

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.TextBackspace)
        {
            if (Editable)
            {
                var changed = false;
                var oldText = _textRope;
                var cursor = _cursorPosition;
                var selectStart = _selectionStart;
                if (_selectionStart != _cursorPosition)
                {
                    TextRope = Rope.Delete(oldText, SelectionLower.Index, SelectionLength);
                    _cursorPosition = SelectionLower;
                    changed = true;
                }
                else if (_cursorPosition.Index != 0)
                {
                    var remPos = _cursorPosition.Index - 1;
                    var remAmt = 1;
                    // If this is a low surrogate remove two chars to remove the whole pair.
                    if (char.IsLowSurrogate(Rope.Index(oldText, remPos)))
                    {
                        remPos -= 1;
                        remAmt = 2;
                    }

                    TextRope = Rope.Delete(oldText, remPos, remAmt);
                    _cursorPosition.Index -= remAmt;
                    changed = true;
                }

                if (changed)
                {
                    _selectionStart = _cursorPosition;
                    OnTextChanged?.Invoke(new CodeEditEventArgs(this, _textRope));
                    // _updatePseudoClass();
                    // OnBackspace?.Invoke(new LineEditBackspaceEventArgs(oldText, _text, cursor, selectStart));
                }

                InvalidateHorizontalCursorPos();

                args.Handle();
            }
        }
        else if (args.Function == EngineKeyFunctions.TextDelete)
        {
            if (Editable)
            {
                var changed = false;
                if (_selectionStart != _cursorPosition)
                {
                    TextRope = Rope.Delete(TextRope, SelectionLower.Index, SelectionLength);
                    _cursorPosition = SelectionLower;
                    changed = true;
                }
                else if (_cursorPosition.Index < TextLength)
                {
                    var remAmt = 1;
                    if (char.IsHighSurrogate(Rope.Index(TextRope, _cursorPosition.Index)))
                        remAmt = 2;

                    TextRope = Rope.Delete(TextRope, _cursorPosition.Index, remAmt);
                    changed = true;
                }

                if (changed)
                {
                    _selectionStart = _cursorPosition;
                    OnTextChanged?.Invoke(new CodeEditEventArgs(this, _textRope));
                    // _updatePseudoClass();
                }

                InvalidateHorizontalCursorPos();

                args.Handle();
            }
        }
        else if (args.Function == EngineKeyFunctions.TextWordBackspace)
        {
            if (Editable)
            {
                var changed = false;

                // If there is a selection, we just delete the selection. Otherwise we delete the previous word
                if (_selectionStart != _cursorPosition)
                {
                    TextRope = Rope.Delete(TextRope, SelectionLower.Index, SelectionLength);
                    _cursorPosition = SelectionLower;
                    changed = true;
                }
                else if (_cursorPosition.Index < TextLength)
                {
                    var runes = Rope.EnumerateRunesReverse(TextRope, _cursorPosition.Index);
                    int remAmt = -CodeEditShared.PrevWordPosition(runes.GetEnumerator());

                    TextRope = Rope.Delete(TextRope, _cursorPosition.Index - remAmt, remAmt);
                    _cursorPosition.Index -= remAmt;
                    changed = true;
                }

                if (changed)
                {
                    _selectionStart = _cursorPosition;
                    OnTextChanged?.Invoke(new CodeEditEventArgs(this, _textRope));
                }

                InvalidateHorizontalCursorPos();
                args.Handle();
            }
        }
        else if (args.Function == EngineKeyFunctions.TextWordDelete)
        {
            if (Editable)
            {
                var changed = false;

                // If there is a selection, we just delete the selection. Otherwise we delete the next word
                if (_selectionStart != _cursorPosition)
                {
                    TextRope = Rope.Delete(TextRope, SelectionLower.Index, SelectionLength);
                    _cursorPosition = SelectionLower;
                    changed = true;
                }
                else if (_cursorPosition.Index < TextLength)
                {
                    var runes = Rope.EnumerateRunes(TextRope, _cursorPosition.Index);
                    int endWord = _cursorPosition.Index + CodeEditShared.EndWordPosition(runes.GetEnumerator());

                    TextRope = Rope.Delete(TextRope, _cursorPosition.Index, endWord - _cursorPosition.Index);
                    changed = true;
                }

                if (changed)
                {
                    _selectionStart = _cursorPosition;
                    OnTextChanged?.Invoke(new CodeEditEventArgs(this, _textRope));
                }

                InvalidateHorizontalCursorPos();
                args.Handle();
            }
        }
        else if (args.Function == EngineKeyFunctions.TextNewline)
        {
            InsertAtCursor("\n");

            InvalidateHorizontalCursorPos();

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.TextSelectAll)
        {
            _cursorPosition = new CursorPos(TextLength, LineBreakBias.Bottom);
            _selectionStart = new CursorPos(0, LineBreakBias.Top);

            InvalidateHorizontalCursorPos();

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIClick || args.Function == EngineKeyFunctions.TextCursorSelect)
        {
            _mouseSelectingText = true;
            _lastMouseSelectPos = args.RelativePosition;

            // Find closest cursor position under mouse.
            var index = GetIndexAtPos(args.RelativePosition);

            _cursorPosition = index;

            if (args.Function != EngineKeyFunctions.TextCursorSelect)
            {
                _selectionStart = _cursorPosition;
            }

            InvalidateHorizontalCursorPos();

            args.Handle();

            doCursorVisible = false;
        }
        else if (args.Function == EngineKeyFunctions.TextCopy)
        {
            var range = SelectionRange;
            var text = Rope.CollapseSubstring(TextRope, range);
            if (text.Length != 0)
            {
                _clipboard.SetText(text);
            }

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.TextCut)
        {
            if (Editable || SelectionLower != SelectionUpper)
            {
                var range = SelectionRange;
                var text = Rope.CollapseSubstring(TextRope, range);
                if (text.Length != 0)
                {
                    _clipboard.SetText(text);
                }

                InsertAtCursor("");
            }

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.TextPaste)
        {
            if (Editable)
            {
                async void DoPaste()
                {
                    // Happens asynchronously, be aware
                    var text = await _clipboard.GetText();
                    InsertAtCursor(text);
                    EnsureCursorVisible();
                }

                DoPaste();
            }

            args.Handle();
            doCursorVisible = false;
        }
        else if (args.Function == EngineKeyFunctions.TextReleaseFocus)
        {
            ReleaseKeyboardFocus();
            args.Handle();
            return;
        }

        if (args.Handled)
        {
            // Reset this so the cursor is always visible immediately after a keybind is pressed.
            _blink.Reset();

            if (doCursorVisible)
                EnsureCursorVisible();
        }
    }

    private void CacheHorizontalCursorPos(CursorPos pos)
    {
        EnsureLineBreaksUpdated();

        if (_horizontalCursorPos != null)
            return;

        _horizontalCursorPos = GetHorizontalPositionAtIndex(pos);
    }

    /// <summary>
    /// Calculate the position the cursor should move to with a certain move.
    /// </summary>
    /// <remarks>
    /// This method is not pure: while it does not modify the actual cursor position yet,
    /// only calculating the next position, it still calls <c>CacheHorizontalCursorPos</c> manually.
    /// </remarks>
    /// <param name="type">The type of move the cursor is doing.</param>
    /// <param name="select">Whether a selection is being expanded.</param>
    /// <param name="keepHorizontalCursorPos">
    /// Whether the cached horizontal cursor position must be kept instead of being invalidates.
    /// </param>
    /// <returns>The new position of the cursor in the text.</returns>
    private CursorPos CalcCursorMove(MoveType type, bool select, out bool keepHorizontalCursorPos)
    {
        DebugTools.Assert(BitOperations.PopCount((uint) type) == 1, "Only a single movement bit may be set in the type");

        keepHorizontalCursorPos = false;

        var breakingSelection = _selectionStart != _cursorPosition && !select;
        switch (type)
        {
            case MoveType.Left:
                {
                    if (breakingSelection)
                    {
                        // If we're breaking an active selection, move to the lower side of it.
                        return SelectionLower;
                    }

                    var (_, lineStart, _) = GetLineForCursorPos(_cursorPosition);

                    if (_cursorPosition.Bias == LineBreakBias.Bottom && _cursorPosition.Index == lineStart)
                    {
                        // Swap cursor bias around to make the cursor appear at the end of the previous line.
                        // This maintains the index, it's the same position in the text
                        return _cursorPosition with { Bias = LineBreakBias.Top };
                    }

                    var newPos = CursorShiftedLeft();
                    // Explicit newlines work kinda funny with bias, so keep it at top there.
                    var bias = _cursorPosition.Index == TextLength || Rope.Index(TextRope, newPos) == '\n'
                        ? LineBreakBias.Top
                        : LineBreakBias.Bottom;

                    return new CursorPos(newPos, bias);
                }
            case MoveType.Right:
                {
                    if (breakingSelection)
                    {
                        return SelectionUpper;
                    }

                    var (_, _, lineEnd) = GetLineForCursorPos(_cursorPosition);
                    // Explicit newlines work kinda funny with bias, so keep it at top there.
                    if (_cursorPosition.Bias == LineBreakBias.Top
                        && _cursorPosition.Index == lineEnd
                        && _cursorPosition.Index != TextLength
                        && Rope.Index(TextRope, _cursorPosition.Index) != '\n')
                    {
                        // Swap cursor bias around to make the cursor appear at the start of the next line.
                        // This maintains the index, it's the same position in the text.
                        return _cursorPosition with { Bias = LineBreakBias.Bottom };
                    }

                    return new CursorPos(CursorShiftedRight(), LineBreakBias.Top);
                }
            case MoveType.LeftWord:
                {
                    var runes = Rope.EnumerateRunesReverse(TextRope, _cursorPosition.Index);
                    var pos = _cursorPosition.Index + CodeEditShared.PrevWordPosition(runes.GetEnumerator());

                    return new CursorPos(pos, LineBreakBias.Bottom);
                }
            case MoveType.RightWord:
                {
                    var runes = Rope.EnumerateRunes(TextRope, _cursorPosition.Index);
                    var pos = _cursorPosition.Index + CodeEditShared.EndWordPosition(runes.GetEnumerator());

                    return new CursorPos(pos, LineBreakBias.Bottom);
                }
            case MoveType.Up:
                {
                    var cursor = _cursorPosition;
                    if (breakingSelection)
                    {
                        // If we're in a selection, we move from the selection LOWER upwards, not the cursor position.
                        InvalidateHorizontalCursorPos();

                        cursor = SelectionLower;
                    }

                    CacheHorizontalCursorPos(cursor);
                    DebugTools.Assert(_horizontalCursorPos.HasValue, "Horizontal cursor pos must be cached!");

                    var (line, _, _) = GetLineForCursorPos(cursor);

                    if (line == 0)
                    {
                        // We're on the top line already, move to the start of it instead.
                        return new CursorPos(0, LineBreakBias.Top);
                    }

                    keepHorizontalCursorPos = true;

                    return GetIndexAtHorizontalPos(line - 1, _horizontalCursorPos!.Value);
                }
            case MoveType.Down:
                {
                    var cursor = _cursorPosition;
                    if (breakingSelection)
                    {
                        // If we're in a selection, we move from the selection LOWER upwards, not the cursor position.
                        InvalidateHorizontalCursorPos();

                        cursor = SelectionUpper;
                    }

                    CacheHorizontalCursorPos(cursor);
                    DebugTools.Assert(_horizontalCursorPos.HasValue, "Horizontal cursor pos must be cached!");

                    var (line, _, _) = GetLineForCursorPos(cursor);

                    if (line == _lineBreaks.Count)
                    {
                        // On the last line already, move to the end of it.
                        return new CursorPos(TextLength, LineBreakBias.Top);
                    }

                    keepHorizontalCursorPos = true;

                    return GetIndexAtHorizontalPos(line + 1, _horizontalCursorPos!.Value);
                }
            case MoveType.BeginOfLine:
                {
                    var (_, lineStart, _) = GetLineForCursorPos(_cursorPosition);
                    if (Rope.Index(TextRope, lineStart) == '\n')
                        lineStart += 1;

                    return new CursorPos(lineStart, LineBreakBias.Bottom);
                }
            case MoveType.EndOfLine:
                {
                    var (_, _, lineEnd) = GetLineForCursorPos(_cursorPosition);
                    return new CursorPos(lineEnd, LineBreakBias.Top);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    [Pure]
    private int CursorShiftedLeft()
    {
        if (_cursorPosition.Index == 0)
            return _cursorPosition.Index;

        return (int) Rope.RuneShiftLeft(_cursorPosition.Index, _textRope);
    }

    [Pure]
    private int CursorShiftedRight()
    {
        if (_cursorPosition.Index == TextLength)
            return _cursorPosition.Index;

        return (int) Rope.RuneShiftRight(_cursorPosition.Index, _textRope);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function == EngineKeyFunctions.UIClick || args.Function == EngineKeyFunctions.TextCursorSelect)
        {
            _mouseSelectingText = false;
        }
    }

    private void InvalidateHorizontalCursorPos()
    {
        _horizontalCursorPos = null;
    }

    protected override void TextEntered(GUITextEnteredEventArgs args)
    {
        base.TextEntered(args);

        if (!Editable)
            return;

        InsertAtCursor(args.Text);
        _blink.Reset();
        EnsureCursorVisible();
        // OnTextTyped?.Invoke(args);
    }

    protected override void TextEditing(GUITextEditingEventArgs args)
    {
        base.TextEditing(args);

        if (!Editable)
            return;

        var ev = args.Event;
        var startChars = ev.GetStartChars();

        // Just break an active composition and build it anew to handle in-progress ones.
        AbortIme();

        if (ev.Text != "")
        {
            if (_selectionStart != _cursorPosition)
            {
                // Delete active text selection.
                InsertAtCursor("");
            }

            var startPos = _cursorPosition;
            TextRope = Rope.Insert(TextRope, startPos.Index, ev.Text);
            OnTextChanged?.Invoke(new CodeEditEventArgs(this, _textRope));

            _selectionStart = _cursorPosition = new CursorPos(startPos.Index + startChars, LineBreakBias.Top);
            _imeData = (startPos, ev.Text.Length);
        }

        EnsureCursorVisible();
    }

    private void AbortIme(bool delete = true)
    {
        if (!_imeData.HasValue)
            return;

        if (delete)
        {
            var (imeStart, imeLength) = _imeData.Value;

            TextRope = Rope.Delete(TextRope, imeStart.Index, imeLength);

            _selectionStart = _cursorPosition = imeStart;
        }

        _imeData = null;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        var renderBoxSize = _renderBox.Size;

        _scrollBar.Page = renderBoxSize.Y * UIScale;

        UpdateLineBreaks((int) (renderBoxSize.X * UIScale));

        return size;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        EnsureLineBreaksUpdated();

        _blink.FrameUpdate(args);

        if (_mouseSelectingText)
        {
            var contentBox = PixelSizeBox;

            var index = GetIndexAtPos(_lastMouseSelectPos);

            _cursorPosition = index;

            // Only move scrollbar when the cursor is dragging above/below the text control.
            if (_lastMouseSelectPos.Y < contentBox.Top)
            {
                EnsureCursorVisible();
            }
            else if (_lastMouseSelectPos.Y > contentBox.Bottom)
            {
                EnsureCursorVisible();
            }
        }
    }

    [Pure]
    private Font GetFont()
    {
        return StylePropertyDefault("font", UserInterfaceManager.ThemeDefaults.DefaultFont);
    }

    [Pure]
    private Color GetFontColor()
    {
        return StylePropertyDefault("font-color", Color.White);
    }

    internal void QueueLineBreakUpdate()
    {
        _lineUpdateQueued = true;
    }

    private void EnsureLineBreaksUpdated()
    {
        if (_lineUpdateQueued)
            UpdateLineBreaks(PixelWidth);
    }

    public void InsertAtCursor(string text)
    {
        // Strip newlines.
        var lower = SelectionLower.Index;
        var upper = SelectionUpper.Index;

        TextRope = Rope.ReplaceSubstring(TextRope, lower, upper - lower, text);
        OnTextChanged?.Invoke(new CodeEditEventArgs(this, _textRope));

        _selectionStart = _cursorPosition = new CursorPos(lower + text.Length, LineBreakBias.Top);
        // OnTextChanged?.Invoke(new LineEditEventArgs(this, _text));
        // _updatePseudoClass();
    }

    private void UpdateLineBreaks(int pixelWidth)
    {
        _lineBreaks.Clear();
        InvalidateHorizontalCursorPos();

        var font = GetFont();
        var scale = UIScale;

        var wordWrap = new WordWrap(pixelWidth);
        int? breakLine;

        foreach (var rune in Rope.EnumerateRunes(GetDisplayRope()))
        {
            wordWrap.NextRune(rune, out breakLine, out var breakNewLine, out var skip);
            CheckLineBreak(breakLine);
            CheckLineBreak(breakNewLine);
            if (skip)
                continue;

            // Uh just skip unknown characters I guess.
            if (!font.TryGetCharMetrics(rune, scale, out var metrics))
                continue;

            wordWrap.NextMetrics(metrics, out breakLine, out var abort);
            CheckLineBreak(breakLine);
            if (abort)
                return;
        }

        wordWrap.FinalizeText(out breakLine);
        CheckLineBreak(breakLine);

        void CheckLineBreak(int? line)
        {
            if (line is { } l)
            {
                _lineBreaks.Add(l);
            }
        }

        // Update scroll bar max size.
        var lineCount = GetLineCount();
        _scrollBar.MaxValue = Math.Max(_scrollBar.Page, lineCount * font.GetLineHeight(scale));

        _lineUpdateQueued = false;
    }

    /// <summary>
    /// Get the rope of text actually being displayed. This may be the placeholder.
    /// </summary>
    private Rope.Node GetDisplayRope()
    {
        if (!Rope.IsNullOrEmpty(_textRope))
            return _textRope;

        return _placeholder ?? Rope.Leaf.Empty;
    }

    private int GetLineCount()
    {
        return _lineBreaks.Count + 1;
    }

    private CursorPos GetIndexAtPos(Vector2 position)
    {
        EnsureLineBreaksUpdated();

        var clickPos = position * UIScale;
        clickPos.Y += _scrollBar.Value;

        var font = GetFont();

        var lineHeight = font.GetLineHeight(UIScale);
        var lineIndex = (int) (clickPos.Y / lineHeight);

        return GetIndexAtHorizontalPos(lineIndex, position.X);
    }

    private CursorPos GetIndexAtHorizontalPos(int line, float horizontalPos)
    {
        // If the placeholder is visible, this function does not return correct results because it looks at TextRope,
        // but _lineBreaks is configured for the display rope.
        // Bail out early in this case, the function is not currently used in any situation in any location
        // where something else is desired if the placeholder is visible.
        if (IsPlaceholderVisible)
            return default;

        var contentBox = PixelSizeBox;
        var font = GetFont();
        var uiScale = UIScale;
        horizontalPos *= uiScale;

        (int, int) FindVerticalLine()
        {
            // Step one: find the vertical line containing the mouse position.

            if (line > _lineBreaks.Count)
            {
                // Below the last line, return the far end of the last line then.
                return (TextLength, TextLength);
            }

            if (line < 0)
            {
                // Above the first line, clamp.
                return (0, 0);
            }

            return (
                line == 0 ? 0 : _lineBreaks[line - 1],
                _lineBreaks.Count == line ? TextLength : _lineBreaks[line]
            );
        }

        // textIdx = start index on the vertical line we're on.
        // breakIdx = where the next line starts.
        var (textIdx, breakIdx) = FindVerticalLine();

        var chrPosX = 0f;
        var lastChrPosX = 0f;
        var index = textIdx;
        foreach (var rune in Rope.EnumerateRunes(TextRope, textIdx))
        {
            if (index >= breakIdx)
            {
                break;
            }

            if (!font.TryGetCharMetrics(rune, uiScale, out var metrics))
            {
                index += rune.Utf16SequenceLength;
                continue;
            }

            if (chrPosX > horizontalPos)
            {
                break;
            }

            lastChrPosX = chrPosX;
            chrPosX += metrics.Advance;
            index += rune.Utf16SequenceLength;

            if (chrPosX > contentBox.Right)
            {
                break;
            }
        }

        // Distance between the right side of the glyph overlapping the mouse and the mouse.
        var distanceRight = chrPosX - horizontalPos;
        // Same but left side.
        var distanceLeft = horizontalPos - lastChrPosX;
        // If the mouse is closer to the left of the glyph we lower the index one, so we select before that glyph.
        if (index > 0 && distanceRight > distanceLeft)
        {
            index = (int) Rope.RuneShiftLeft(index, TextRope);
        }

        return new CursorPos(index, index == textIdx ? LineBreakBias.Bottom : LineBreakBias.Top);
    }

    private float GetHorizontalPositionAtIndex(CursorPos pos)
    {
        EnsureLineBreaksUpdated();

        var hPos = 0;
        var font = GetFont();
        var uiScale = UIScale;

        var (_, lineStart, _) = GetLineForCursorPos(pos);
        using var runeEnumerator = Rope.EnumerateRunes(TextRope, lineStart).GetEnumerator();

        var i = lineStart;
        while (true)
        {
            if (i >= pos.Index)
                break;

            if (!runeEnumerator.MoveNext())
                break;

            var rune = runeEnumerator.Current;
            if (font.TryGetCharMetrics(rune, uiScale, out var metrics))
                hPos += metrics.Advance;

            i += rune.Utf16SequenceLength;
        }

        return hPos / uiScale;
    }

    private (int lineIdx, int lineStart, int lineEnd) GetLineForCursorPos(CursorPos pos)
    {
        DebugTools.Assert(pos.Index >= 0);

        EnsureLineBreaksUpdated();

        if (_lineBreaks.Count == 0)
            return (0, 0, TextLength);

        int i;
        for (i = 0; i < _lineBreaks.Count; i++)
        {
            var lineIdx = _lineBreaks[i];
            if (pos.Bias == LineBreakBias.Bottom ? (lineIdx > pos.Index) : (lineIdx >= pos.Index))
            {
                if (i == 0)
                {
                    // First line
                    return (0, 0, lineIdx);
                }

                return (i, _lineBreaks[i - 1], lineIdx);
            }
        }

        // Position is on last line.
        return (_lineBreaks.Count, _lineBreaks[^1], TextLength);
    }

    private int GetStartOfLine(int lineIndex)
    {
        if (lineIndex <= 0)
        {
            // First line: start of text
            return 0;
        }

        if (lineIndex > _lineBreaks.Count)
        {
            // Past the last line: just put it at text end so nothing happens I guess.
            return TextLength;
        }

        return _lineBreaks[lineIndex - 1];
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        _lastDebugMousePos = null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        _lastDebugMousePos = args.RelativePosition;
        _lastMouseSelectPos = args.RelativePosition;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (MathHelper.CloseToPercent(0, args.Delta.Y))
            return;

        _scrollBar.ValueTarget -= GetScrollSpeed() * args.Delta.Y;
    }

    [Pure]
    private float GetScrollSpeed()
    {
        return GetFont().GetLineHeight(UIScale) * 2;
    }

    private void EnsureCursorVisible()
    {
        EnsureLineBreaksUpdated();

        var font = GetFont();

        var scrollOffset = _scrollBar.Value;
        var (cursorLine, _, _) = GetLineForCursorPos(_cursorPosition);

        var cursorMargin = font.GetLineHeight(UIScale) * 1.5f;
        var (lineT, lineB) = GetBoundsOfLine(cursorLine);

        // Give the cursor some margin so it's not *right* up at the visible edge.
        lineT -= cursorMargin;
        lineB += cursorMargin;

        // Vertical boundaries of the vertical section of text.
        var visibleT = scrollOffset;
        var visibleB = scrollOffset + PixelSize.Y;

        // Make the scroll bar move to a position where the cursor is visible within margin.

        if (lineT < visibleT)
        {
            // Part of the line is ABOVE the visible region, move scrollbar UP.

            _scrollBar.ValueTarget = lineT;
        }
        else if (lineB > visibleB)
        {
            // Part of the line is BELOW the visible region, move scrollbar DOWN.

            _scrollBar.ValueTarget = lineB - PixelHeight;
        }
    }

    private (float start, float end) GetBoundsOfLine(int line)
    {
        var font = GetFont();
        var lineHeight = font.GetLineHeight(UIScale);
        return (lineHeight * line, lineHeight * (line + 1));
    }

    private void UpdatePseudoClass()
    {
        SetOnlyStylePseudoClass(IsPlaceholderVisible ? StylePseudoClassPlaceholder : null);
        if (!Editable)
            AddStylePseudoClass(StylePseudoClassNotEditable);
    }

    protected override void KeyboardFocusEntered()
    {
        base.KeyboardFocusEntered();

        _blink.Reset();

        if (Editable)
        {
            Root?.Window?.TextInputStart();
        }
    }

    protected override void KeyboardFocusExited()
    {
        base.KeyboardFocusExited();

        Root?.Window?.TextInputStop();
        AbortIme(delete: false);
    }
}
