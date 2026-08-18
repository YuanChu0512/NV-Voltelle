using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed class VfOffsetChange
    {
        public int Index { get; set; }
        public int FrequencyOffsetKHz { get; set; }
    }

    internal sealed class VfCurveContract
    {
        public VfCurveContract()
        {
            Points = new List<VfPointSnapshot>();
        }

        public IList<VfPointSnapshot> Points { get; private set; }
    }

    internal static class NvApiVfLayouts
    {
        // The 256-bit mask has one spare bit. The 0x2420 Control layout stores
        // 255 entries (indices 0..254) after its 0x44-byte header.
        internal const int PointSlotCount = 255;
        internal const int ExpectedUsablePointCount = 127;

        private const int MaskOffset = 0x04;
        private const int MaskSize = 0x20;
        private const int InfoEntriesOffset = 0x44;
        private const int InfoEntryStride = 0x18;
        private const int StatusEntriesOffset = 0x68;
        private const int StatusEntryStride = 0x15C;
        private const int ControlEntriesOffset = 0x44;
        private const int ControlEntryStride = 0x24;
        private const int ControlOffsetField = 0x14;

        internal static byte[] CreateInfoRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.VfInfoSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.VfInfoVersionWord);
            return buffer;
        }

        internal static byte[] CreateStatusRequest(byte[] info)
        {
            RequireInfo(info);
            byte[] buffer = new byte[PrivateNvApiContracts.VfStatusSizeRtx50];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.VfStatusVersionWordRtx50);
            Buffer.BlockCopy(info, MaskOffset, buffer, MaskOffset, MaskSize);
            return buffer;
        }

        internal static byte[] CreateControlRequest(byte[] info)
        {
            RequireInfo(info);
            byte[] buffer = new byte[PrivateNvApiContracts.VfControlSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.VfControlVersionWord);
            Buffer.BlockCopy(info, MaskOffset, buffer, MaskOffset, MaskSize);
            return buffer;
        }

        internal static VfCurveContract ParseCurve(byte[] info, byte[] status, byte[] control)
        {
            RequireInfo(info);
            RequireBuffer(status, PrivateNvApiContracts.VfStatusSizeRtx50, PrivateNvApiContracts.VfStatusVersionWordRtx50, "V/F Status");
            RequireBuffer(control, PrivateNvApiContracts.VfControlSize, PrivateNvApiContracts.VfControlVersionWord, "V/F Control");
            if (status[0x24] != 1)
                throw new InvalidOperationException("V/F Status +0x24 不是 mVolt 预期的 1。");
            if (!IsSupportedInfoMask(info))
                throw new InvalidOperationException("V/F Info mask 不是 RTX 50 已知的 129/132-bit 形态。");
            RequireEqualMask(info, status, "Status");
            RequireEqualMask(info, control, "Control");

            VfCurveContract curve = new VfCurveContract();
            uint previousVoltage = 0;
            for (int index = 0; index < PointSlotCount; index++)
            {
                if (!IsMaskBitSet(info, index)) continue;

                int infoEntry = InfoEntriesOffset + index * InfoEntryStride;
                int statusEntry = StatusEntriesOffset + index * StatusEntryStride;
                int controlEntry = ControlEntriesOffset + index * ControlEntryStride;
                uint infoKind = NvApiTuningLayouts.ReadUInt32(info, infoEntry);
                uint statusKind = NvApiTuningLayouts.ReadUInt32(status, statusEntry);
                uint controlKind = NvApiTuningLayouts.ReadUInt32(control, controlEntry);
                if (infoKind != 0 || statusKind != 0 || controlKind != 0 || info[infoEntry + 4] != 1)
                    continue;

                uint actualFrequency = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x04);
                uint voltage = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x08);
                uint baseFrequency = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x0C);
                uint duplicateVoltage = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x10);
                int offset = NvApiTuningLayouts.ReadInt32(control, controlEntry + ControlOffsetField);

                if (actualFrequency < 1 || actualFrequency > 6000000 ||
                    baseFrequency < 1 || baseFrequency > 6000000)
                    throw new InvalidOperationException("V/F 点 " + index + " 的频率超出 1..6000000 kHz。");
                if (voltage < 250000 || voltage > 1500000 || duplicateVoltage != voltage)
                    throw new InvalidOperationException("V/F 点 " + index + " 的电压字段不符合契约。");
                if (offset < -1000000 || offset > 1000000)
                    throw new InvalidOperationException("V/F 点 " + index + " 的 offset 超出 -1000000..1000000 kHz。");
                if (curve.Points.Count != 0 && voltage <= previousVoltage)
                    throw new InvalidOperationException("V/F 有效点电压没有严格递增。");

                curve.Points.Add(new VfPointSnapshot
                {
                    Index = index,
                    VoltageUv = voltage,
                    BaseFrequencyKHz = baseFrequency,
                    ActualFrequencyKHz = actualFrequency,
                    FrequencyOffsetKHz = offset
                });
                previousVoltage = voltage;
            }

            if (curve.Points.Count != ExpectedUsablePointCount)
                throw new InvalidOperationException("V/F 有效点数量为 " + curve.Points.Count + "，mVolt 预期 127。");
            for (int index = 0; index < curve.Points.Count; index++)
                if (curve.Points[index].Index < 0 || curve.Points[index].Index >= PointSlotCount)
                    throw new InvalidOperationException("V/F 点索引无效。");
            return curve;
        }

        internal static byte[] CreateControlSet(byte[] currentControl, IList<VfPointSnapshot> currentPoints, IList<VfOffsetChange> changes)
        {
            RequireBuffer(currentControl, PrivateNvApiContracts.VfControlSize, PrivateNvApiContracts.VfControlVersionWord, "V/F Control");
            if (currentPoints == null) throw new ArgumentNullException("currentPoints");
            if (changes == null) throw new ArgumentNullException("changes");
            if (changes.Count == 0) throw new ArgumentException("至少需要一个 V/F 目标点。", "changes");

            Dictionary<int, VfPointSnapshot> points = new Dictionary<int, VfPointSnapshot>();
            for (int index = 0; index < currentPoints.Count; index++)
            {
                VfPointSnapshot point = currentPoints[index];
                if (point == null || point.Index < 0 || point.Index >= PointSlotCount)
                    throw new ArgumentException("currentPoints 包含无效点。", "currentPoints");
                points[point.Index] = point;
            }

            byte[] result = (byte[])currentControl.Clone();
            Array.Clear(result, MaskOffset, MaskSize);
            HashSet<int> seen = new HashSet<int>();
            for (int index = 0; index < changes.Count; index++)
            {
                VfOffsetChange change = changes[index];
                if (change == null) throw new ArgumentException("changes 包含 null。", "changes");
                VfPointSnapshot point;
                if (!points.TryGetValue(change.Index, out point))
                    throw new ArgumentOutOfRangeException("changes", "V/F 点 " + change.Index + " 不在当前曲线中。");
                if (!seen.Add(change.Index))
                    throw new ArgumentException("V/F 点 " + change.Index + " 重复。", "changes");
                ValidateOffset(point, change.FrequencyOffsetKHz);
                SetMaskBit(result, change.Index);
                int field = ControlEntriesOffset + change.Index * ControlEntryStride + ControlOffsetField;
                NvApiTuningLayouts.WriteInt32(result, field, change.FrequencyOffsetKHz);
            }
            return result;
        }

        internal static byte[] CreateSinglePointRestore(byte[] currentControl, IList<VfPointSnapshot> currentPoints, int pointIndex)
        {
            RequireBuffer(currentControl, PrivateNvApiContracts.VfControlSize, PrivateNvApiContracts.VfControlVersionWord, "V/F Control");
            if (currentPoints == null) throw new ArgumentNullException("currentPoints");
            VfPointSnapshot selected = null;
            for (int index = 0; index < currentPoints.Count; index++)
            {
                VfPointSnapshot point = currentPoints[index];
                if (point == null || point.Index < 0 || point.Index >= PointSlotCount)
                    throw new ArgumentException("currentPoints 包含无效点。", "currentPoints");
                if (point.Index == pointIndex) selected = point;
            }
            if (selected == null)
                throw new ArgumentOutOfRangeException("pointIndex", "V/F 恢复点不在当前曲线中。");
            return CreateControlSet(
                currentControl,
                currentPoints,
                new VfOffsetChange[]
                {
                    new VfOffsetChange { Index = pointIndex, FrequencyOffsetKHz = selected.FrequencyOffsetKHz }
                });
        }

        private static void ValidateOffset(VfPointSnapshot point, int offsetKHz)
        {
            if (offsetKHz < -1000000 || offsetKHz > 1000000)
                throw new ArgumentOutOfRangeException("offsetKHz", "V/F offset 超出 -1000000..1000000 kHz。");
            long requestedFrequency = (long)point.BaseFrequencyKHz + offsetKHz;
            if (requestedFrequency < 1 || requestedFrequency > 6000000)
                throw new ArgumentOutOfRangeException("offsetKHz", "基准频率加 offset 超出 1..6000000 kHz。");
        }

        private static bool IsSupportedInfoMask(byte[] info)
        {
            for (int word = 0; word < 8; word++)
            {
                uint value = NvApiTuningLayouts.ReadUInt32(info, MaskOffset + word * 4);
                uint mask132 = word < 4 ? UInt32.MaxValue : (word == 4 ? 0x0FU : 0U);
                uint mask129 = word < 4 ? UInt32.MaxValue : (word == 4 ? 0x01U : 0U);
                if (value != mask132)
                {
                    bool all129 = true;
                    for (int check = 0; check < 8; check++)
                    {
                        uint actual = NvApiTuningLayouts.ReadUInt32(info, MaskOffset + check * 4);
                        uint expected = check < 4 ? UInt32.MaxValue : (check == 4 ? 0x01U : 0U);
                        if (actual != expected) { all129 = false; break; }
                    }
                    return all129;
                }
            }
            return true;
        }

        private static bool IsMaskBitSet(byte[] buffer, int index)
        {
            uint word = NvApiTuningLayouts.ReadUInt32(buffer, MaskOffset + (index >> 5) * 4);
            return (word & (1U << (index & 31))) != 0;
        }

        private static void SetMaskBit(byte[] buffer, int index)
        {
            int offset = MaskOffset + (index >> 5) * 4;
            uint word = NvApiTuningLayouts.ReadUInt32(buffer, offset);
            NvApiTuningLayouts.WriteUInt32(buffer, offset, word | (1U << (index & 31)));
        }

        private static void RequireEqualMask(byte[] info, byte[] other, string name)
        {
            for (int index = 0; index < MaskSize; index++)
                if (info[MaskOffset + index] != other[MaskOffset + index])
                    throw new InvalidOperationException("V/F " + name + " mask 与 Info 不一致。");
        }

        private static void RequireInfo(byte[] info)
        {
            RequireBuffer(info, PrivateNvApiContracts.VfInfoSize, PrivateNvApiContracts.VfInfoVersionWord, "V/F Info");
        }

        private static void RequireBuffer(byte[] buffer, int size, uint version, string name)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (buffer.Length != size)
                throw new ArgumentException(name + " 大小为 0x" + buffer.Length.ToString("X") + "，预期 0x" + size.ToString("X") + "。", "buffer");
            uint actual = NvApiTuningLayouts.ReadUInt32(buffer, 0);
            if (actual != version)
                throw new InvalidOperationException(name + " 版本字为 0x" + actual.ToString("X8") + "，预期 0x" + version.ToString("X8") + "。");
        }
    }
}
