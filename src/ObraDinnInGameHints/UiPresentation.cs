using System;
using System.Collections.Generic;

namespace ObraDinnInGameHints
{
    internal static class UiPresentation
    {
        public static float[] CenterButtonPositions(IList<float> widths, float gap)
        {
            if (widths == null || widths.Count == 0)
                return new float[0];

            float safeGap = Math.Max(0f, gap);
            float total = safeGap * Math.Max(0, widths.Count - 1);
            for (int i = 0; i < widths.Count; i++)
                total += Math.Max(0f, widths[i]);

            float cursor = -total * 0.5f;
            float[] positions = new float[widths.Count];
            for (int i = 0; i < widths.Count; i++)
            {
                float width = Math.Max(0f, widths[i]);
                positions[i] = cursor + width * 0.5f;
                cursor += width + safeGap;
            }
            return positions;
        }

        // The native game cursor may be created lazily. Retry only while the
        // panel is open and the previous binding is absent or its object died.
        public static bool ShouldRetryNativeCursor(bool panelOpen, bool bindingCaptured, bool cursorObjectAlive)
        {
            return panelOpen && (!bindingCaptured || !cursorObjectAlive);
        }

        public static bool ShouldOverrideMouseInput(bool panelOpen)
        {
            return panelOpen;
        }

        public static bool ShouldRestoreMouseState(bool panelOpen, bool stateCaptured)
        {
            return !panelOpen && stateCaptured;
        }
    }
}
