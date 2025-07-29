using System.Collections;
using System.Diagnostics.Contracts;
using System.Text;
using Robust.Client.Graphics;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._EE.UserInterface.Controls.CodeEdit;

internal static class CodeEditShared
{
    // Approach for NextWordPosition and PrevWordPosition taken from Avalonia.

    //
    // Functions for calculating next positions when doing word-bound cursor movement (ctrl+left/right).
    //

    internal static int EndWordPosition(string str, int cursor)
    {
        return cursor + EndWordPosition(new StringEnumerateHelpers.SubstringRuneEnumerator(str, cursor));
    }

    internal static int EndWordPosition<T>(T runes) where T : IEnumerator<Rune>
    {
        if (!runes.MoveNext())
            return 0;

        var i = 0;
        if (!IterForward(CharClass.Whitespace))
            return i;

        var charClass = GetCharClass(runes.Current);
        IterForward(charClass);

        return i;

        bool IterForward(CharClass cClass)
        {
            var hasNext = true;

            do
            {
                var rune = runes.Current;

                if (GetCharClass(rune) != cClass)
                    break;

                i += rune.Utf16SequenceLength;

                hasNext = runes.MoveNext();
            } while (hasNext);

            return hasNext;
        }
    }

    internal static int PrevWordPosition(string str, int cursor)
    {
        return cursor + PrevWordPosition(new StringEnumerateHelpers.SubstringReverseRuneEnumerator(str, cursor));
    }

    internal static int PrevWordPosition<T>(T runes) where T : IEnumerator<Rune>
    {
        if (!runes.MoveNext())
            return 0;

        var startRune = runes.Current;
        var charClass = GetCharClass(startRune);

        var i = 0;
        var keepGoing = IterBackward();

        if (keepGoing && charClass == CharClass.Whitespace)
        {
            charClass = GetCharClass(runes.Current);

            IterBackward();
        }

        return i;

        bool IterBackward()
        {
            do
            {
                var rune = runes.Current;

                if (GetCharClass(rune) != charClass)
                    return true;

                i -= rune.Utf16SequenceLength;
            } while (runes.MoveNext());

            return false;
        }
    }

    private static CharClass GetCharClass(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune))
        {
            return CharClass.Whitespace;
        }

        if (Rune.IsLetterOrDigit(rune))
        {
            return CharClass.AlphaNumeric;
        }

        return CharClass.Other;
    }

    private enum CharClass : byte
    {
        Other,
        AlphaNumeric,
        Whitespace
    }

    /// <summary>
    /// Helper type for the cursor blink animation.
    /// </summary>
    internal struct CursorBlink
    {
        /// <summary>
        /// The total length of the animation.
        /// </summary>
        private const float BlinkTime = 1.3f;

        // Because of the animation curves used, there is a plateau on either end of the animation.
        // 0 or t/2 in the animation, and you are exactly in the middle of this plateau.
        // Now, when we reset the blink (i.e. when the user presses a button),
        // we want this plateau to stay for a bit longer. So we offset it by this start time in that case.
        private const float BlinkStartTime = BlinkTime * -0.2f;
        private const float HalfBlinkTime = BlinkTime / 2;

        public float Opacity;
        public float Timer;

        public void Reset()
        {
            Timer = BlinkTime + BlinkStartTime;
            UpdateOpacity();
        }

        public void FrameUpdate(FrameEventArgs args)
        {
            Timer += args.DeltaSeconds;
            UpdateOpacity();
        }

        private void UpdateOpacity()
        {
            if (Timer >= BlinkTime)
                Timer %= BlinkTime;

            // Manually implement the animation function with easings. The math isn't thaaaaaaaat bad right?

            if (Timer < HalfBlinkTime)
            {
                // First half: cursor is dimming.
                Opacity = 1 - Easings.InOutQuint(Timer * (1 / HalfBlinkTime));
            }
            else
            {
                // Second half: cursor is brightening again.
                Opacity = Easings.InOutQuint((Timer - HalfBlinkTime) * (1 / HalfBlinkTime));
            }
        }
    }
}

internal static class StringEnumerateHelpers
{
    internal struct SubstringRuneEnumerator : IEnumerable<Rune>, IEnumerator<Rune>
    {
        private readonly string _source;
        private int _nextChar;
        private Rune _current;

        public SubstringRuneEnumerator(string source, int firstChar)
        {
            _source = source;
            _nextChar = firstChar;
            _current = default;
        }

        public bool MoveNext()
        {
            if (_nextChar >= _source.Length)
                return false;

            if (!Rune.TryGetRuneAt(_source, _nextChar, out _current))
                _current = Rune.ReplacementChar;

            _nextChar += _current.Utf16SequenceLength;
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public readonly Rune Current => _current;

        object IEnumerator.Current => Current;

        public void Dispose()
        {
            // Nada.
        }

        public SubstringRuneEnumerator GetEnumerator() => this;

        IEnumerator<Rune> IEnumerable<Rune>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal struct SubstringReverseRuneEnumerator : IEnumerator<Rune>, IEnumerable<Rune>
    {
        private string _source;
        // Contains the next char to return.
        // If the next char is actually a (valid) surrogate pair, this is INSIDE the pair,
        // and MoveNext() has to skip more.
        private int _nextChar;
        private Rune _current;

        public SubstringReverseRuneEnumerator(string source, int startChar)
        {
            _source = source;
            _nextChar = startChar - 1;
            _current = default;
        }

        public bool MoveNext()
        {
            if (_nextChar < 0)
                return false;

            var chr = _source[_nextChar];
            if (!char.IsSurrogate(chr))
            {
                _current = new Rune(chr);
            }
            else if (char.IsLowSurrogate(chr) && _nextChar >= 1)
            {
                var prevChr = _source[_nextChar - 1];
                if (char.IsHighSurrogate(prevChr))
                    _current = new Rune(prevChr, chr);
                else
                    _current = Rune.ReplacementChar;
            }
            else
            {
                _current = Rune.ReplacementChar;
            }

            _nextChar -= _current.Utf16SequenceLength;
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public Rune Current => _current;

        object IEnumerator.Current => Current;

        public void Dispose()
        {
            // Nada.
        }

        public SubstringReverseRuneEnumerator GetEnumerator() => this;

        IEnumerator<Rune> IEnumerable<Rune>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

internal struct WordWrap
{
    private readonly float _maxSizeX;

    public float MaxUsedWidth;
    // Index we put into the LineBreaks list when a line break should occur.
    public int BreakIndexCounter;
    public int NextBreakIndexCounter;
    // If the CURRENT processing word ends up too long, this is the index to put a line break.
    public (int index, float lineSize)? WordStartBreakIndex;
    // Word size in pixels.
    public int WordSizePixels;
    // The horizontal position of the text cursor.
    public int PosX;
    public Rune LastRune;
    // If a word is larger than maxSizeX, we split it.
    // We need to keep track of some data to split it into two words.
    public (int breakIndex, int wordSizePixels)? ForceSplitData = null;

    public WordWrap(float maxSizeX)
    {
        this = default;
        _maxSizeX = maxSizeX;
        LastRune = new Rune('A');
    }

    public void NextRune(Rune rune, out int? breakLine, out int? breakNewLine, out bool skip)
    {
        BreakIndexCounter = NextBreakIndexCounter;
        NextBreakIndexCounter += rune.Utf16SequenceLength;

        breakLine = null;
        breakNewLine = null;
        skip = false;

        if (IsWordBoundary(LastRune, rune) || rune == new Rune('\n'))
        {
            // Word boundary means we know where the word ends.
            if (PosX > _maxSizeX && LastRune != new Rune(' '))
            {
                DebugTools.Assert(WordStartBreakIndex.HasValue,
                    "wordStartBreakIndex can only be null if the word begins at a new line, in which case this branch shouldn't be reached as the word would be split due to being longer than a single line.");
                //Ensure the assert had a chance to run and then just return
                if (!WordStartBreakIndex.HasValue)
                    return;

                // We ran into a word boundary and the word is too big to fit the previous line.
                // So we insert the line break BEFORE the last word.
                breakLine = WordStartBreakIndex!.Value.index;
                MaxUsedWidth = Math.Max(MaxUsedWidth, WordStartBreakIndex.Value.lineSize);
                PosX = WordSizePixels;
            }

            // Start a new word since we hit a word boundary.
            //wordSize = 0;
            WordSizePixels = 0;
            WordStartBreakIndex = (BreakIndexCounter, PosX);
            ForceSplitData = null;

            // Just manually handle newlines.
            if (rune == new Rune('\n'))
            {
                MaxUsedWidth = Math.Max(MaxUsedWidth, PosX);
                PosX = 0;
                WordStartBreakIndex = null;
                skip = true;
                breakNewLine = BreakIndexCounter;
            }
        }

        LastRune = rune;
    }

    public void NextMetrics(in CharMetrics metrics, out int? breakLine, out bool abort)
    {
        abort = false;
        breakLine = null;

        // Increase word size and such with the current character.
        var oldWordSizePixels = WordSizePixels;
        WordSizePixels += metrics.Advance;
        // TODO: Theoretically, does it make sense to break after the glyph's width instead of its advance?
        //   It might result in some more tight packing but I doubt it'd be noticeable.
        //   Also definitely even more complex to implement.
        PosX += metrics.Advance;

        if (PosX <= _maxSizeX)
            return;

        if (!ForceSplitData.HasValue)
        {
            ForceSplitData = (BreakIndexCounter, oldWordSizePixels);
        }

        // Oh hey we get to break a word that doesn't fit on a single line.
        if (WordSizePixels > _maxSizeX)
        {
            var (breakIndex, splitWordSize) = ForceSplitData.Value;
            if (splitWordSize == 0)
            {
                // Happens if there's literally not enough space for a single character so uh...
                // Yeah just don't.
                abort = true;
                return;
            }

            // Reset forceSplitData so that we can split again if necessary.
            ForceSplitData = null;
            breakLine = breakIndex;
            WordSizePixels -= splitWordSize;
            WordStartBreakIndex = null;
            MaxUsedWidth = Math.Max(MaxUsedWidth, _maxSizeX);
            PosX = WordSizePixels;
        }
    }

    public int FinalizeText(out int? breakLine)
    {
        // This needs to happen because word wrapping doesn't get checked for the last word.
        if (PosX > _maxSizeX)
        {
            if (!WordStartBreakIndex.HasValue)
            {
                Logger.Error(
                    "Assert fail inside RichTextEntry.Update, " +
                    "wordStartBreakIndex is null on method end w/ word wrap required. " +
                    "Dumping relevant stuff. Send this to PJB.");
                // Logger.Error($"Message: {Message}");
                Logger.Error($"maxSizeX: {_maxSizeX}");
                Logger.Error($"maxUsedWidth: {MaxUsedWidth}");
                Logger.Error($"breakIndexCounter: {BreakIndexCounter}");
                Logger.Error("wordStartBreakIndex: null (duh)");
                Logger.Error($"wordSizePixels: {WordSizePixels}");
                Logger.Error($"posX: {PosX}");
                Logger.Error($"lastChar: {LastRune}");
                Logger.Error($"forceSplitData: {ForceSplitData}");
                // Logger.Error($"LineBreaks: {string.Join(", ", LineBreaks)}");

                throw new Exception(
                    "wordStartBreakIndex can only be null if the word begins at a new line," +
                    "in which case this branch shouldn't be reached as" +
                    "the word would be split due to being longer than a single line.");
            }

            breakLine = WordStartBreakIndex.Value.index;
            MaxUsedWidth = Math.Max(MaxUsedWidth, WordStartBreakIndex.Value.lineSize);
        }
        else
        {
            breakLine = null;
            MaxUsedWidth = Math.Max(MaxUsedWidth, PosX);
        }

        return (int) MaxUsedWidth;
    }

    [Pure]
    private static bool IsWordBoundary(Rune a, Rune b)
    {
        return a == new Rune(' ') || b == new Rune(' ') || a == new Rune('-') || b == new Rune('-');
    }

}
