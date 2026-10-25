using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SpeechPresentationStateTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("speech reveal waits for exact playback identity and advances a monotonic word prefix", new Action(() => {
                var state = new SpeechPresentationState("request-1", "conversation-1", "person-1", "Hello, friend.");
                IList<TextWeightedWordSpan> words = Spans(
                    new TextWeightedWordSpan(0, 6, 0, 10), new TextWeightedWordSpan(7, 7, 10, 20));

                Check.Equal(SpeechPresentationMode.Thinking, state.Mode);
                Check.True(!state.TryStartInitialPlayback("wrong", "conversation-1", "person-1", "Hello, friend.", 1, 48000, 20, words));
                Check.True(state.TryStartInitialPlayback("request-1", "conversation-1", "person-1", "Hello, friend.", 1, 48000, 20, words));
                state.AdvancePlayback(1, 0);
                Check.Equal(SpeechPresentationMode.InitialReveal, state.Mode);
                Check.Equal("Hello,", state.VisibleText);
                Check.Equal(0, state.CurrentWordStartUtf16);
                Check.Equal(6, state.CurrentWordEndUtf16);

                state.AdvancePlayback(1, 15);
                Check.Equal("Hello, friend.", state.VisibleText);
                Check.Equal(7, state.CurrentWordStartUtf16);
                state.AdvancePlayback(1, 5);
                Check.Equal("Hello, friend.", state.VisibleText);
                Check.Equal(7, state.CurrentWordStartUtf16);
                state.CompletePlayback(1);
                Check.Equal(SpeechPresentationMode.FullText, state.Mode);
                Check.Equal("Hello, friend.", state.VisibleText);
                Check.Equal(-1, state.CurrentWordStartUtf16);
            })));

            cases.Add(("speech replay keeps full canonical text and only current replay word blue", new Action(() => {
                var state = new SpeechPresentationState("request-2", "conversation-1", "person-1", "one two three", true);
                IList<TextWeightedWordSpan> words = Spans(
                    new TextWeightedWordSpan(0, 3, 0, 10), new TextWeightedWordSpan(4, 3, 10, 20),
                    new TextWeightedWordSpan(8, 5, 20, 30));
                Check.True(state.TryStartReplay("request-2", "conversation-1", "person-1", "one two three", 2, 48000, 30, words));
                Check.Equal("one two three", state.VisibleText);
                state.AdvancePlayback(2, 25);
                Check.Equal("one two three", state.VisibleText);
                Check.Equal(8, state.CurrentWordStartUtf16);
                state.AdvancePlayback(2, 5);
                Check.Equal(8, state.CurrentWordStartUtf16);
                Check.Equal("one two three", state.VisibleText);
                state.CompletePlayback(2);
                Check.Equal(-1, state.CurrentWordStartUtf16);
            })));

            cases.Add(("speech reveal preserves completed words across skipped frames and silence gaps", new Action(() => {
                const string exact = "one two three";
                var state = new SpeechPresentationState("request-gap", "conversation-1", "person-1", exact);
                IList<TextWeightedWordSpan> words = Spans(
                    new TextWeightedWordSpan(0, 3, 0, 10), new TextWeightedWordSpan(4, 3, 20, 30),
                    new TextWeightedWordSpan(8, 5, 40, 50));
                Check.True(state.TryStartInitialPlayback("request-gap", "conversation-1", "person-1", exact,
                    1, 48000, 60, words));
                // First UI sample skips the first word's end and lands in silence.
                state.AdvancePlayback(1, 15);
                Check.Equal("one", state.VisibleText);
                Check.Equal(-1, state.CurrentWordStartUtf16);
                // Skip the second word too, landing in another gap: completed prefix remains.
                state.AdvancePlayback(1, 35);
                Check.Equal("one two", state.VisibleText);
                Check.Equal(-1, state.CurrentWordStartUtf16);
                // Past the final word but before total duration still reveals it without blue.
                state.AdvancePlayback(1, 55);
                Check.Equal(exact, state.VisibleText);
                Check.Equal(-1, state.CurrentWordStartUtf16);
                state.AdvancePlayback(1, 45);
                Check.Equal(exact, state.VisibleText);
                Check.Equal(-1, state.CurrentWordStartUtf16);
            })));

            cases.Add(("speech fallback exposes exact Unicode text without splitting graphemes", new Action(() => {
                const string exact = "café 東京 😀 e\u0301";
                var state = new SpeechPresentationState("request-3", "conversation-2", "person-3", exact);
                IList<TextWeightedWordSpan> unsafeWords = Spans(new TextWeightedWordSpan(0, 4, 0, 10));
                Check.True(!state.TryStartInitialPlayback("request-3", "conversation-2", "person-3", exact,
                    1, 48000, 10, unsafeWords));
                Check.Equal(SpeechPresentationMode.Thinking, state.Mode);
                state.ResolveWithoutAudio();
                Check.Equal(SpeechPresentationMode.FullText, state.Mode);
                Check.Equal(exact, state.VisibleText);
            })));

            cases.Add(("speech reveal resolves no-TTS and pre-start failure to exact full text", new Action(() => {
                const string exact = "Good day.";
                var state = new SpeechPresentationState("request-4", "conversation-3", "person-4", exact);
                state.ResolveWithoutAudio();
                Check.Equal(SpeechPresentationMode.FullText, state.Mode);
                Check.Equal(exact, state.VisibleText);
            })));
        }

        private static IList<TextWeightedWordSpan> Spans(params TextWeightedWordSpan[] values)
        { return values; }
    }
}
