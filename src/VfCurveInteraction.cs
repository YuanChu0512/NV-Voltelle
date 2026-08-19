using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal static class VfCurveInteraction
    {
        internal static int TargetMHzFromVerticalPosition(
            VfPointSnapshot point,
            double pointerY,
            double plotTop,
            double plotBottom,
            double axisMinimumKHz,
            double axisMaximumKHz)
        {
            if (point == null) throw new ArgumentNullException("point");
            if (plotBottom <= plotTop) throw new ArgumentOutOfRangeException("plotBottom");
            if (axisMaximumKHz <= axisMinimumKHz) throw new ArgumentOutOfRangeException("axisMaximumKHz");
            double boundedY = Math.Max(plotTop, Math.Min(plotBottom, pointerY));
            double frequencyKHz = axisMaximumKHz -
                (boundedY - plotTop) * (axisMaximumKHz - axisMinimumKHz) / (plotBottom - plotTop);
            return ClampTargetMHz(point, (int)Math.Round(frequencyKHz / 1000.0));
        }

        internal static int ClampTargetMHz(VfPointSnapshot point, int requestedTargetMHz)
        {
            if (point == null) throw new ArgumentNullException("point");
            long minimumOffset = Math.Max(-1000000L, 1L - point.BaseFrequencyKHz);
            long maximumOffset = Math.Min(1000000L, 6000000L - point.BaseFrequencyKHz);
            int minimumTarget = (int)Math.Ceiling((point.BaseFrequencyKHz + minimumOffset) / 1000.0);
            int maximumTarget = (int)Math.Floor((point.BaseFrequencyKHz + maximumOffset) / 1000.0);
            return Math.Max(Math.Max(1, minimumTarget), Math.Min(maximumTarget, requestedTargetMHz));
        }

        internal static int ClampUniformDeltaMHz(
            IList<VfPointSnapshot> points,
            IList<int> selectedPointIndices,
            IDictionary<int, int> startingOffsetsKHz,
            int requestedDeltaMHz)
        {
            if (points == null) throw new ArgumentNullException("points");
            if (selectedPointIndices == null || selectedPointIndices.Count == 0)
                throw new ArgumentException("至少选择一个 V/F 点。", "selectedPointIndices");
            if (startingOffsetsKHz == null) throw new ArgumentNullException("startingOffsetsKHz");

            long minimumDeltaKHz = Int64.MinValue;
            long maximumDeltaKHz = Int64.MaxValue;
            HashSet<int> found = new HashSet<int>();
            for (int selectedIndex = 0; selectedIndex < selectedPointIndices.Count; selectedIndex++)
            {
                int pointIndex = selectedPointIndices[selectedIndex];
                if (!found.Add(pointIndex)) continue;
                VfPointSnapshot point = null;
                for (int pointListIndex = 0; pointListIndex < points.Count; pointListIndex++)
                {
                    if (points[pointListIndex].Index != pointIndex) continue;
                    point = points[pointListIndex];
                    break;
                }
                if (point == null)
                    throw new ArgumentOutOfRangeException("selectedPointIndices", "选区包含不存在的 V/F 点。");
                int startingOffset;
                if (!startingOffsetsKHz.TryGetValue(pointIndex, out startingOffset))
                    startingOffset = point.FrequencyOffsetKHz;
                long effectiveKHz = checked((long)point.BaseFrequencyKHz + startingOffset);
                long pointMinimum = Math.Max(-1000000L - startingOffset, 1L - effectiveKHz);
                long pointMaximum = Math.Min(1000000L - startingOffset, 6000000L - effectiveKHz);
                minimumDeltaKHz = Math.Max(minimumDeltaKHz, pointMinimum);
                maximumDeltaKHz = Math.Min(maximumDeltaKHz, pointMaximum);
            }

            int minimumDeltaMHz = (int)Math.Ceiling(minimumDeltaKHz / 1000.0);
            int maximumDeltaMHz = (int)Math.Floor(maximumDeltaKHz / 1000.0);
            if (minimumDeltaMHz > maximumDeltaMHz)
                throw new InvalidOperationException("所选 V/F 点没有共同的可平移范围。");
            return Math.Max(minimumDeltaMHz, Math.Min(maximumDeltaMHz, requestedDeltaMHz));
        }

        internal static void RemoveSuccessfulDrafts(IList<VfOffsetChange> drafts, IList<string> successfulStepLabels)
        {
            if (drafts == null) throw new ArgumentNullException("drafts");
            if (successfulStepLabels == null || successfulStepLabels.Count == 0) return;
            HashSet<int> successful = new HashSet<int>();
            for (int labelIndex = 0; labelIndex < successfulStepLabels.Count; labelIndex++)
            {
                string label = successfulStepLabels[labelIndex];
                if (label == null || !label.StartsWith("点 ", StringComparison.Ordinal)) continue;
                int pointIndex;
                if (Int32.TryParse(label.Substring(2), out pointIndex)) successful.Add(pointIndex);
            }
            for (int draftIndex = drafts.Count - 1; draftIndex >= 0; draftIndex--)
                if (drafts[draftIndex] != null && successful.Contains(drafts[draftIndex].Index)) drafts.RemoveAt(draftIndex);
        }
    }
}
