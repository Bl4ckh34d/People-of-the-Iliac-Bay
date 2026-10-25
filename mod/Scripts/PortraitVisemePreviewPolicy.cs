using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.DFU
{
    public struct PortraitAtlasCell
    {
        public readonly float UMin;
        public readonly float VMin;
        public readonly float UWidth;
        public readonly float VHeight;

        public PortraitAtlasCell(float uMin, float vMin, float uWidth, float vHeight)
        {
            UMin = uMin;
            VMin = vMin;
            UWidth = uWidth;
            VHeight = vHeight;
        }
    }

    public static class PortraitAtlasGrid
    {
        public static PortraitAtlasCell TopLeftIndexedCell(int index, int columns, int rows)
        {
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
            if (index < 0 || index >= checked(columns * rows)) throw new ArgumentOutOfRangeException(nameof(index));
            int column = index % columns;
            int rowFromTop = index / columns;
            float cellWidth = 1f / columns;
            float cellHeight = 1f / rows;
            return new PortraitAtlasCell(column * cellWidth, 1f - (rowFromTop + 1) * cellHeight,
                cellWidth, cellHeight);
        }
    }

    public enum PortraitRestoreAction
    {
        KeepCurrentPanel,
        RestoreCurrentPortrait,
        RestoreCachedPortrait,
        ClearPrototypeComposite,
    }

    /// <summary>Pure safety policy for removing a prototype composite from the talk panel.</summary>
    public static class PortraitVisemeRestorePolicy
    {
        public static PortraitRestoreAction Decide(bool panelIsPrototypeComposite, bool conversationStillBound,
            bool actorMatchesBinding, bool faceMatchesBinding, bool currentPortraitAvailable)
        {
            if (!panelIsPrototypeComposite) return PortraitRestoreAction.KeepCurrentPanel;
            if (!conversationStillBound || !actorMatchesBinding || !faceMatchesBinding)
                return PortraitRestoreAction.ClearPrototypeComposite;
            return currentPortraitAvailable
                ? PortraitRestoreAction.RestoreCurrentPortrait
                : PortraitRestoreAction.RestoreCachedPortrait;
        }
    }

    /// <summary>Counts visible shape transitions and permits one QA capture per open shape/request.</summary>
    public sealed class PortraitVisemePreviewCueCounter
    {
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> capturedOpenShapes = new HashSet<string>(StringComparer.Ordinal);

        public bool Record(string requestId, PortraitMouthShape shape, out int count, out bool capture)
        {
            count = 0;
            capture = false;
            if (string.IsNullOrWhiteSpace(requestId)) return false;
            string key = requestId + "|" + shape;
            counts.TryGetValue(key, out count);
            count++;
            counts[key] = count;
            if (shape != PortraitMouthShape.X) capture = capturedOpenShapes.Add(key);
            return true;
        }
    }
}
