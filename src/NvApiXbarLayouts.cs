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

    internal sealed class ClockDomainDescriptor
    {
        public string Name { get; set; }
        public uint DomainId { get; set; }
        public uint InfoFlag { get; set; }
        public int ControlSelector { get; set; }
        public int ControlMarkerOffset { get; set; }
        public int ControlOffsetField { get; set; }
    }

    internal static class NvApiXbarLayouts
    {
        private const int InfoEntryCount = 32;
        private const int InfoEntriesOffset = 0xB0;
        private const int InfoEntryStride = 0x430;
        internal static readonly ClockDomainDescriptor Crossbar = new ClockDomainDescriptor
        {
            Name = "Crossbar", DomainId = 1U, InfoFlag = 2U, ControlSelector = 2,
            ControlMarkerOffset = 0x428, ControlOffsetField = 0x53C
        };
        internal static readonly ClockDomainDescriptor Sys = new ClockDomainDescriptor
        {
            Name = "SYS", DomainId = 2U, InfoFlag = 8U, ControlSelector = 8,
            ControlMarkerOffset = 0xA30, ControlOffsetField = 0xB44
        };
        internal static readonly ClockDomainDescriptor Video = new ClockDomainDescriptor
        {
            Name = "Video", DomainId = 21U, InfoFlag = 0x10U, ControlSelector = 0x10,
            ControlMarkerOffset = 0xD34, ControlOffsetField = 0xE48
        };

        internal static byte[] CreateInfoRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.XbarInfoSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.XbarInfoVersionWord);
            return buffer;
        }

        internal static XbarInfoContract ParseInfo(byte[] buffer)
        {
            return ParseInfo(buffer, Crossbar);
        }

        internal static XbarInfoContract ParseInfo(byte[] buffer, ClockDomainDescriptor domain)
        {
            if (domain == null) throw new ArgumentNullException("domain");
            RequireBuffer(buffer, PrivateNvApiContracts.XbarInfoSize, PrivateNvApiContracts.XbarInfoVersionWord, domain.Name + " Info");
            uint flags = NvApiTuningLayouts.ReadUInt32(buffer, 0x08);
            if ((flags & domain.InfoFlag) == 0)
                throw new InvalidOperationException(domain.Name + " Info flags 未设置 0x" + domain.InfoFlag.ToString("X") + "。");

            for (int index = 0; index < InfoEntryCount; index++)
            {
                int entry = InfoEntriesOffset + index * InfoEntryStride;
                if (entry + 0x40 > buffer.Length) break;
                if (NvApiTuningLayouts.ReadUInt32(buffer, entry) != domain.DomainId) continue;
                uint packed = NvApiTuningLayouts.ReadUInt32(buffer, entry + 0x3C);
                int minimum = unchecked((short)(packed & 0xFFFFU));
                int maximum = unchecked((short)(packed >> 16));
                if (minimum < -2000 || maximum > 2000 || minimum > maximum)
                    throw new InvalidOperationException(domain.Name + " offset 范围不符合 -2000..2000 MHz 契约。");
                return new XbarInfoContract
                {
                    Flags = flags,
                    MinimumOffsetMHz = minimum,
                    MaximumOffsetMHz = maximum,
                    EntryIndex = index
                };
            }
            throw new InvalidOperationException(domain.Name + " Info 未找到 domain " + domain.DomainId + " 条目。");
        }

        internal static byte[] CreateControlRequest()
        {
            return CreateControlRequest(Crossbar);
        }

        internal static byte[] CreateControlRequest(ClockDomainDescriptor domain)
        {
            if (domain == null) throw new ArgumentNullException("domain");
            byte[] buffer = new byte[PrivateNvApiContracts.XbarControlSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.XbarControlVersionWord);
            NvApiTuningLayouts.WriteInt32(buffer, 0x08, domain.ControlSelector);
            return buffer;
        }

        internal static XbarControlContract ParseControl(byte[] buffer)
        {
            return ParseControl(buffer, Crossbar);
        }

        internal static XbarControlContract ParseControl(byte[] buffer, ClockDomainDescriptor domain)
        {
            RequireControl(buffer, domain);
            return new XbarControlContract
            {
                CurrentOffsetKHz = NvApiTuningLayouts.ReadInt32(buffer, domain.ControlOffsetField)
            };
        }

        internal static byte[] CreateControlSet(byte[] currentControl, XbarInfoContract info, int requestedOffsetMHz)
        {
            return CreateControlSet(currentControl, info, requestedOffsetMHz, Crossbar);
        }

        internal static byte[] CreateControlSet(byte[] currentControl, XbarInfoContract info, int requestedOffsetMHz, ClockDomainDescriptor domain)
        {
            RequireControl(currentControl, domain);
            if (info == null) throw new ArgumentNullException("info");
            if (requestedOffsetMHz < info.MinimumOffsetMHz || requestedOffsetMHz > info.MaximumOffsetMHz)
                throw new ArgumentOutOfRangeException("requestedOffsetMHz", domain.Name + " offset 超出驱动范围。");
            for (int offset = domain.ControlOffsetField + 4; offset <= domain.ControlOffsetField + 0x10; offset += 4)
                if (NvApiTuningLayouts.ReadUInt32(currentControl, offset) != 0)
                    throw new InvalidOperationException(domain.Name + " Control 0x" + offset.ToString("X") + " 保留字段不是 0。");

            int requestedKHz;
            try
            {
                requestedKHz = checked(requestedOffsetMHz * 1000);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException("requestedOffsetMHz", domain.Name + " MHz 转 kHz 溢出。");
            }
            byte[] result = (byte[])currentControl.Clone();
            NvApiTuningLayouts.WriteInt32(result, domain.ControlOffsetField, requestedKHz);
            return result;
        }

        internal static byte[] CreateMeasureRequest()
        {
            return CreateMeasureRequest(Crossbar);
        }

        internal static byte[] CreateMeasureRequest(ClockDomainDescriptor domain)
        {
            if (domain == null) throw new ArgumentNullException("domain");
            byte[] buffer = new byte[PrivateNvApiContracts.XbarMeasureSize];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.XbarMeasureVersionWord);
            NvApiTuningLayouts.WriteUInt32(buffer, 0x04, domain.DomainId);
            return buffer;
        }

        internal static uint ParseMeasuredFrequency(byte[] buffer)
        {
            return ParseMeasuredFrequency(buffer, Crossbar);
        }

        internal static uint ParseMeasuredFrequency(byte[] buffer, ClockDomainDescriptor domain)
        {
            if (domain == null) throw new ArgumentNullException("domain");
            RequireBuffer(buffer, PrivateNvApiContracts.XbarMeasureSize, PrivateNvApiContracts.XbarMeasureVersionWord, domain.Name + " Measure");
            if (NvApiTuningLayouts.ReadUInt32(buffer, 0x04) != domain.DomainId)
                throw new InvalidOperationException(domain.Name + " Measure +0x04 不是 " + domain.DomainId + "。");
            return NvApiTuningLayouts.ReadUInt32(buffer, 0x08);
        }

        private static void RequireControl(byte[] buffer, ClockDomainDescriptor domain)
        {
            if (domain == null) throw new ArgumentNullException("domain");
            RequireBuffer(buffer, PrivateNvApiContracts.XbarControlSize, PrivateNvApiContracts.XbarControlVersionWord, domain.Name + " Control");
            if (NvApiTuningLayouts.ReadUInt32(buffer, 0x08) != (uint)domain.ControlSelector)
                throw new InvalidOperationException(domain.Name + " Control +0x08 不是 " + domain.ControlSelector + "。");
            if (NvApiTuningLayouts.ReadUInt32(buffer, domain.ControlMarkerOffset) != 0x0FU)
                throw new InvalidOperationException(domain.Name + " Control +0x" + domain.ControlMarkerOffset.ToString("X") + " 不是 0x0F。");
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
