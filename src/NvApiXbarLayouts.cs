using System;

namespace MVolt.Rebuild
{
    internal sealed class XbarInfoContract
    {
        public uint Flags { get; set; }
        public int MinimumOffsetMHz { get; set; }
        public int MaximumOffsetMHz { get; set; }
        public int EntryIndex { get; set; }
    }

    internal sealed class XbarControlContract
    {
        public int CurrentOffsetKHz { get; set; }
    }

    internal static class NvApiXbarLayouts
    {
        private const int InfoEntryCount = 32;
        private const int InfoEntriesOffset = 0xB0;
        private const int InfoEntryStride = 0x430;
        private const int ControlOffsetField = 0x53C;

        internal static byte[] CreateInfoRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.XbarInfoSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.XbarInfoVersionWord);
            return buffer;
        }

        internal static XbarInfoContract ParseInfo(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.XbarInfoSize, PrivateNvApiContracts.XbarInfoVersionWord, "Crossbar Info");
            uint flags = NvApiTuningLayouts.ReadUInt32(buffer, 0x08);
            if ((flags & 2U) == 0)
                throw new InvalidOperationException("Crossbar Info flags 未设置 bit 1。");

            for (int index = 0; index < InfoEntryCount; index++)
            {
                int entry = InfoEntriesOffset + index * InfoEntryStride;
                if (entry + 0x40 > buffer.Length) break;
                if (NvApiTuningLayouts.ReadUInt32(buffer, entry) != 1U) continue;
                uint packed = NvApiTuningLayouts.ReadUInt32(buffer, entry + 0x3C);
                int minimum = unchecked((short)(packed & 0xFFFFU));
                int maximum = unchecked((short)(packed >> 16));
                if (minimum < -2000 || maximum > 2000 || minimum > maximum)
                    throw new InvalidOperationException("Crossbar offset 范围不符合 -2000..2000 MHz 契约。");
                return new XbarInfoContract
                {
                    Flags = flags,
                    MinimumOffsetMHz = minimum,
                    MaximumOffsetMHz = maximum,
                    EntryIndex = index
                };
            }
            throw new InvalidOperationException("Crossbar Info 未找到类型 1 条目。");
        }

        internal static byte[] CreateControlRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.XbarControlSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.XbarControlVersionWord);
            NvApiTuningLayouts.WriteInt32(buffer, 0x08, 2);
            return buffer;
        }

        internal static XbarControlContract ParseControl(byte[] buffer)
        {
            RequireControl(buffer);
            return new XbarControlContract
            {
                CurrentOffsetKHz = NvApiTuningLayouts.ReadInt32(buffer, ControlOffsetField)
            };
        }

        internal static byte[] CreateControlSet(byte[] currentControl, XbarInfoContract info, int requestedOffsetMHz)
        {
            RequireControl(currentControl);
            if (info == null) throw new ArgumentNullException("info");
            if (requestedOffsetMHz < info.MinimumOffsetMHz || requestedOffsetMHz > info.MaximumOffsetMHz)
                throw new ArgumentOutOfRangeException("requestedOffsetMHz", "Crossbar offset 超出驱动范围。");
            for (int offset = 0x540; offset <= 0x54C; offset += 4)
                if (NvApiTuningLayouts.ReadUInt32(currentControl, offset) != 0)
                    throw new InvalidOperationException("Crossbar Control 0x" + offset.ToString("X") + " 保留字段不是 0。");

            int requestedKHz;
            try
            {
                requestedKHz = checked(requestedOffsetMHz * 1000);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException("requestedOffsetMHz", "Crossbar MHz 转 kHz 溢出。");
            }
            byte[] result = (byte[])currentControl.Clone();
            NvApiTuningLayouts.WriteInt32(result, ControlOffsetField, requestedKHz);
            return result;
        }

        internal static byte[] CreateMeasureRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.XbarMeasureSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.XbarMeasureVersionWord);
            NvApiTuningLayouts.WriteInt32(buffer, 0x04, 1);
            return buffer;
        }

        internal static uint ParseMeasuredFrequency(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.XbarMeasureSize, PrivateNvApiContracts.XbarMeasureVersionWord, "Crossbar Measure");
            if (NvApiTuningLayouts.ReadUInt32(buffer, 0x04) != 1U)
                throw new InvalidOperationException("Crossbar Measure +0x04 不是 1。");
            return NvApiTuningLayouts.ReadUInt32(buffer, 0x08);
        }

        private static void RequireControl(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.XbarControlSize, PrivateNvApiContracts.XbarControlVersionWord, "Crossbar Control");
            if (NvApiTuningLayouts.ReadUInt32(buffer, 0x08) != 2U)
                throw new InvalidOperationException("Crossbar Control +0x08 不是 2。");
            if (NvApiTuningLayouts.ReadUInt32(buffer, 0x428) != 0x0FU)
                throw new InvalidOperationException("Crossbar Control +0x428 不是 0x0F。");
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
