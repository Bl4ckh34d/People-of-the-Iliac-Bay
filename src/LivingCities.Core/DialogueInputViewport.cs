using System;

namespace LivingCities.Core
{
    // Keeps complete text in the editor; only the drawable substring changes.
    public sealed class DialogueInputViewport
    {
        public int Start { get; private set; }
        public string VisibleText { get; private set; } = "";
        public float CaretX { get; private set; }

        public void Update(string text, int caret, float width, Func<string, float> measure)
        {
            text = text ?? "";
            caret = Math.Max(0, Math.Min(text.Length, caret));
            width = Math.Max(0, width);
            Start = Math.Max(0, Math.Min(Start, caret));
            while (Start < caret && measure(text.Substring(Start, caret - Start)) > width) Start++;
            // Backfill when deleting text, but do not jump while moving the caret.
            if (caret == text.Length)
                while (Start > 0 && measure(text.Substring(Start - 1)) <= width) Start--;
            int end = Start;
            while (end < text.Length && measure(text.Substring(Start, end + 1 - Start)) <= width) end++;
            VisibleText = text.Substring(Start, end - Start);
            CaretX = Math.Min(width, measure(text.Substring(Start, caret - Start)));
        }
    }
}
