using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class TextWeightedWordProgressTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("text-weighted progress uses exact text spans and authoritative audio frame count", new Action(() => {
                Check.True(TextWeightedWordProgress.TryCreate("I pause now.", 1200, out TextWeightedWordProgress plan));
                Check.Equal(3, plan.Words.Count);
                Check.Equal(new TextWeightedWordSpan(0, 1, 0, 109), plan.Words[0]);
                Check.Equal(new TextWeightedWordSpan(2, 5, 109, 654), plan.Words[1]);
                Check.Equal(new TextWeightedWordSpan(8, 4, 654, 1200), plan.Words[2]);
                Check.Equal(1, plan.GetCumulativeTextEnd(99));
                Check.Equal(7, plan.GetCumulativeTextEnd(653));
                Check.Equal(12, plan.GetCumulativeTextEnd(654));
                Check.Equal(12, plan.GetCumulativeTextEnd(1200));
            })));

            cases.Add(("text-weighted progress assigns deterministic pause weight to trailing punctuation", new Action(() => {
                Check.True(TextWeightedWordProgress.TryCreate("longword, I.", 120, out TextWeightedWordProgress plan));
                Check.Equal(new TextWeightedWordSpan(0, 9, 0, 90), plan.Words[0]);
                Check.Equal(new TextWeightedWordSpan(10, 2, 90, 120), plan.Words[1]);
            })));

            cases.Add(("text-weighted progress preserves UTF-16 spans for common curly English punctuation", new Action(() => {
                Check.True(TextWeightedWordProgress.TryCreate("“I’m ready.”", 90, out TextWeightedWordProgress plan));
                Check.Equal(new TextWeightedWordSpan(0, 4, 0, 20), plan.Words[0]);
                Check.Equal(new TextWeightedWordSpan(5, 7, 20, 90), plan.Words[1]);
            })));

            cases.Add(("text-weighted progress clamps cursor and handles extreme bounded frame counts", new Action(() => {
                Check.True(TextWeightedWordProgress.TryCreate("one two", long.MaxValue, out TextWeightedWordProgress plan));
                Check.Equal(0, plan.GetCumulativeTextEnd(-1));
                Check.Equal(0, plan.GetCumulativeTextEnd(0));
                Check.Equal(3, plan.GetCumulativeTextEnd(1));
                // Cursor-to-text mapping is pure; the runtime overlay owns replay high-water state.
                Check.Equal(0, plan.GetCumulativeTextEnd(0));
                Check.Equal(3, plan.GetCumulativeTextEnd(plan.Words[1].StartSample - 1));
                Check.Equal(7, plan.GetCumulativeTextEnd(plan.Words[1].StartSample));
                Check.Equal(7, plan.GetCumulativeTextEnd(long.MaxValue));
            })));

            cases.Add(("text-weighted progress rejects unsupported text and frame budgets", new Action(() => {
                Check.True(!TextWeightedWordProgress.TryCreate("", 100, out _));
                Check.True(!TextWeightedWordProgress.TryCreate("one two", 1, out _));
                Check.True(!TextWeightedWordProgress.TryCreate("café", 100, out _));
                Check.True(!TextWeightedWordProgress.TryCreate("東京", 100, out _));
                Check.True(!TextWeightedWordProgress.TryCreate("one", 0, out _));
                var tokens = new string[513];
                for (int index = 0; index < tokens.Length; index++) tokens[index] = "a";
                Check.True(!TextWeightedWordProgress.TryCreate(string.Join(" ", tokens), 1000, out _));
            })));
        }
    }
}
