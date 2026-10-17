using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public sealed class DialogueWrappedViewport
    {
        public sealed class Line { public int Start, Length; }
        public readonly List<Line> Lines = new List<Line>();
        public int FirstLine { get; private set; }
        public int CaretLine { get; private set; }
        public float CaretX { get; private set; }

        public void Update(string text, int caret, float width, int rows, Func<string, float> measure)
        {
            text = text ?? "";
            caret = Math.Max(0, Math.Min(text.Length, caret));
            width = Math.Max(1, width); rows = Math.Max(1, rows);
            Lines.Clear();
            int start = 0;
            while (start < text.Length)
            {
                int end = start, wordBreak = -1;
                while (end < text.Length && measure(text.Substring(start, end - start + 1)) <= width)
                {
                    if (char.IsWhiteSpace(text[end])) wordBreak = end + 1;
                    end++;
                }
                if (end < text.Length && wordBreak > start) end = wordBreak;
                if (end == start) end++;
                Lines.Add(new Line { Start = start, Length = end - start });
                start = end;
            }
            if (Lines.Count == 0) Lines.Add(new Line());
            CaretLine = Lines.Count - 1;
            for (int i = 0; i < Lines.Count; i++)
                if (caret < Lines[i].Start + Lines[i].Length) { CaretLine = i; break; }
            var line = Lines[CaretLine];
            CaretX = measure(text.Substring(line.Start, caret - line.Start));
            FirstLine = Math.Max(0, Math.Min(FirstLine, Lines.Count - rows));
            if (CaretLine < FirstLine) FirstLine = CaretLine;
            if (CaretLine >= FirstLine + rows) FirstLine = CaretLine - rows + 1;
        }

        public int HitTest(float x, int row, string text, Func<string, float> measure)
        {
            var line = Lines[Math.Max(0, Math.Min(Lines.Count - 1, row))];
            for (int i = 0; i < line.Length; i++)
            {
                float left = measure(text.Substring(line.Start, i));
                float right = measure(text.Substring(line.Start, i + 1));
                if (x < (left + right) / 2) return line.Start + i;
            }
            return line.Start + line.Length;
        }
    }
}
