using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Native editing and focus, with word-wrapped rendering in the classic question panel.
    public sealed class WrappedDialogueInput : TextBox
    {
        static readonly FieldInfo caretField = typeof(TextBox).GetField("cursorPosition", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo setCaret = typeof(TextBox).GetMethod("SetCursorPosition", BindingFlags.Instance | BindingFlags.NonPublic);
        readonly DialogueWrappedViewport viewport = new DialogueWrappedViewport();
        public int VisibleFirstLine { get { return viewport.FirstLine; } }
        public int LineCount { get { return viewport.Lines.Count; } }
        public float VisibleCaretX { get { return viewport.CaretX; } }
        public int Caret { get { return (int)caretField.GetValue(this); } }
        float LineHeight { get { return Font.GlyphHeight + 1; } }
        int Rows { get { return Math.Max(1, (int)((Size.y - TextOffset * 2) / LineHeight)); } }
        float Measure(string text) { return Font.CalculateTextWidth(text, Vector2.one); }
        void Layout() { viewport.Update(Text, Caret, Size.x - TextOffset * 2 - 2, Rows, Measure); }

        public override void Update()
        {
            base.Update();
            if (ReadOnly || !HasFocus() || IMECompositionInProgress) return;
            var key = DaggerfallUI.Instance.LastKeyCode;
            if (key != KeyCode.UpArrow && key != KeyCode.DownArrow) return;
            Layout();
            int row = Math.Max(0, Math.Min(viewport.Lines.Count - 1,
                viewport.CaretLine + (key == KeyCode.UpArrow ? -1 : 1)));
            setCaret.Invoke(this, new object[] { viewport.HitTest(viewport.CaretX, row, Text, Measure) });
        }

        protected override void MouseClick(Vector2 position)
        {
            base.MouseClick(position);
            Layout();
            int row = viewport.FirstLine + (int)Math.Max(0, (position.y - TextOffset) / LineHeight);
            setCaret.Invoke(this, new object[] { viewport.HitTest(position.x - TextOffset, row, Text, Measure) });
        }

        public override void Draw()
        {
            Layout();
            var rect = Rectangle;
            // TextLabel leaves its clipping rectangle on the shared font material.
            // The preceding transcript clips out this upper panel unless we set our own.
            var material = Font.GetMaterial();
            var previousClip = material.GetVector("_ScissorRect");
            material.SetVector("_ScissorRect", new Vector4(rect.xMin / Screen.width, rect.xMax / Screen.width,
                1f - rect.yMax / Screen.height, 1f - rect.yMin / Screen.height));
            try
            {
            if (Text.Length == 0 && !HasFocus())
                Font.DrawText("Click to write. Enter to send.", new Vector2(rect.x + TextOffset * LocalScale.x,
                    rect.y + TextOffset * LocalScale.y), LocalScale, new Color(.65f, .72f, .78f));
            for (int row = viewport.FirstLine; row < Math.Min(viewport.Lines.Count, viewport.FirstLine + Rows); row++)
            {
                var line = viewport.Lines[row];
                Font.DrawText(Text.Substring(line.Start, line.Length),
                    new Vector2(rect.x + TextOffset * LocalScale.x,
                        rect.y + (TextOffset + (row - viewport.FirstLine) * LineHeight) * LocalScale.y),
                    LocalScale, DaggerfallUI.DaggerfallQuestionTextColor);
            }
            Cursor.Enabled = !ReadOnly && (!UseFocus || HasFocus());
            if (Cursor.Enabled)
            {
                Cursor.Position = new Vector2(TextOffset + viewport.CaretX,
                    TextOffset + (viewport.CaretLine - viewport.FirstLine) * LineHeight);
                Cursor.Draw();
            }
            }
            finally { material.SetVector("_ScissorRect", previousClip); }
        }
    }
}
