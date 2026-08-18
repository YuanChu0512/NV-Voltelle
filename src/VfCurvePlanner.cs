using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal static class VfCurvePlanner
    {
        internal static IList<VfOffsetChange> PlanPointTarget(
            IList<VfPointSnapshot> points,
            int pointIndex,
            int targetFrequencyMHz)
        {
            VfPointSnapshot point = FindPoint(points, pointIndex);
            long targetKHz = checked((long)targetFrequencyMHz * 1000L);
            long requestedOffset = targetKHz - point.BaseFrequencyKHz;
            return new VfOffsetChange[]
            {
                new VfOffsetChange
                {
                    Index = point.Index,
                    FrequencyOffsetKHz = ValidateOffset(point, requestedOffset)
                }
            };
        }

        internal static IList<VfOffsetChange> PlanRegionalOffset(
            IList<VfPointSnapshot> points,
            int firstPointIndex,
            int lastPointIndex,
            int offsetMHz)
        {
            RequireCurve(points);
            if (firstPointIndex > lastPointIndex)
                throw new ArgumentException("V/F 区域起点不能晚于终点。");
            long offsetKHz = checked((long)offsetMHz * 1000L);
            List<VfOffsetChange> result = new List<VfOffsetChange>();
            for (int index = 0; index < points.Count; index++)
            {
                VfPointSnapshot point = points[index];
                if (point.Index < firstPointIndex || point.Index > lastPointIndex) continue;
                result.Add(new VfOffsetChange
                {
                    Index = point.Index,
                    FrequencyOffsetKHz = ValidateOffset(point, offsetKHz)
                });
            }
            if (result.Count == 0)
                throw new ArgumentOutOfRangeException("firstPointIndex", "选定区域没有有效 V/F 点。");
            return result;
        }

        internal static IList<VfOffsetChange> PlanUniformTranslation(
            IList<VfPointSnapshot> points,
            IList<int> selectedPointIndices,
            IDictionary<int, int> startingOffsetsKHz,
            int deltaMHz)
        {
            RequireCurve(points);
            if (selectedPointIndices == null || selectedPointIndices.Count == 0)
                throw new ArgumentException("至少选择一个 V/F 点。", "selectedPointIndices");
            if (startingOffsetsKHz == null)
                throw new ArgumentNullException("startingOffsetsKHz");

            HashSet<int> selected = new HashSet<int>(selectedPointIndices);
            long deltaKHz = checked((long)deltaMHz * 1000L);
            List<VfOffsetChange> result = new List<VfOffsetChange>();
            for (int index = 0; index < points.Count; index++)
            {
                VfPointSnapshot point = points[index];
                if (!selected.Contains(point.Index)) continue;
                int startingOffset;
                if (!startingOffsetsKHz.TryGetValue(point.Index, out startingOffset))
                    startingOffset = point.FrequencyOffsetKHz;
                result.Add(new VfOffsetChange
                {
                    Index = point.Index,
                    FrequencyOffsetKHz = ValidateOffset(point, checked((long)startingOffset + deltaKHz))
                });
            }
            if (result.Count != selected.Count)
                throw new ArgumentOutOfRangeException("selectedPointIndices", "选区包含不存在的 V/F 点。残缺选区不会执行。");
            return result;
        }

        internal static IList<VfOffsetChange> PlanFlattenAbove(
            IList<VfPointSnapshot> points,
            int anchorPointIndex)
        {
            RequireCurve(points);
            VfPointSnapshot anchor = FindPoint(points, anchorPointIndex);
            long flatEffectiveFrequency = (long)anchor.BaseFrequencyKHz + anchor.FrequencyOffsetKHz;
            List<VfOffsetChange> result = new List<VfOffsetChange>();
            for (int index = 0; index < points.Count; index++)
            {
                VfPointSnapshot point = points[index];
                if (point.VoltageUv < anchor.VoltageUv) continue;
                long requestedOffset = flatEffectiveFrequency - point.BaseFrequencyKHz;
                try
                {
                    result.Add(new VfOffsetChange
                    {
                        Index = point.Index,
                        FrequencyOffsetKHz = ValidateOffset(point, requestedOffset)
                    });
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new InvalidOperationException(
                        "拉平会使 V/F 点 " + point.Index + " 超出 ±1000 MHz 或有效频率范围。",
                        ex);
                }
            }
            if (result.Count == 0)
                throw new InvalidOperationException("拉平区域为空。");
            return result;
        }

        internal static IList<VfOffsetChange> PlanReset(IList<VfPointSnapshot> points)
        {
            RequireCurve(points);
            List<VfOffsetChange> result = new List<VfOffsetChange>();
            for (int index = 0; index < points.Count; index++)
            {
                VfPointSnapshot point = points[index];
                result.Add(new VfOffsetChange
                {
                    Index = point.Index,
                    FrequencyOffsetKHz = ValidateOffset(point, 0)
                });
            }
            return result;
        }

        private static VfPointSnapshot FindPoint(IList<VfPointSnapshot> points, int pointIndex)
        {
            RequireCurve(points);
            for (int index = 0; index < points.Count; index++)
                if (points[index].Index == pointIndex) return points[index];
            throw new ArgumentOutOfRangeException("pointIndex", "V/F 点不存在。");
        }

        private static int ValidateOffset(VfPointSnapshot point, long offsetKHz)
        {
            if (offsetKHz < -1000000L || offsetKHz > 1000000L)
                throw new ArgumentOutOfRangeException("offsetKHz", "V/F 点 offset 超出 ±1000 MHz。");
            long effective = (long)point.BaseFrequencyKHz + offsetKHz;
            if (effective < 1L || effective > 6000000L)
                throw new ArgumentOutOfRangeException("offsetKHz", "V/F 点有效频率超出 1..6000000 kHz。");
            return checked((int)offsetKHz);
        }

        private static void RequireCurve(IList<VfPointSnapshot> points)
        {
            if (points == null) throw new ArgumentNullException("points");
            if (points.Count != NvApiVfLayouts.ExpectedUsablePointCount)
                throw new ArgumentException("V/F 曲线必须包含 127 个有效点。", "points");
            HashSet<int> indices = new HashSet<int>();
            uint previousVoltage = 0;
            for (int index = 0; index < points.Count; index++)
            {
                VfPointSnapshot point = points[index];
                if (point == null || !indices.Add(point.Index))
                    throw new ArgumentException("V/F 曲线包含无效或重复点。", "points");
                if (index != 0 && point.VoltageUv <= previousVoltage)
                    throw new ArgumentException("V/F 曲线电压没有严格递增。", "points");
                previousVoltage = point.VoltageUv;
            }
        }
    }
}
