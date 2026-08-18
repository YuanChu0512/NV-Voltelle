using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed class PowerMonitorChannelInfo
    {
        public int ChannelIndex { get; set; }
        public uint InfoField0 { get; set; }
        public uint RailId { get; set; }
        public string RailName { get; set; }
    }

    internal sealed class PowerMonitorInfoContract
    {
        public PowerMonitorInfoContract()
        {
            Channels = new List<PowerMonitorChannelInfo>();
        }

        public bool Supported { get; set; }
        public uint Mask { get; set; }
        public int PrimaryChannelIndex { get; set; }
        public IList<PowerMonitorChannelInfo> Channels { get; private set; }

        public PowerMonitorChannelInfo FindChannel(int channelIndex)
        {
            for (int index = 0; index < Channels.Count; index++)
                if (Channels[index].ChannelIndex == channelIndex) return Channels[index];
            return null;
        }
    }

    internal sealed class PowerMonitorChannelSample
    {
        public int ChannelIndex { get; set; }
        public uint InfoField0 { get; set; }
        public uint RailId { get; set; }
        public string RailName { get; set; }
        public uint PowerMilliwatts { get; set; }
        public uint CurrentMilliamps { get; set; }
        public uint VoltageMicrovolts { get; set; }
        public ulong CumulativeEnergyMillijoules { get; set; }
        public double PowerWatts { get { return PowerMilliwatts / 1000.0; } }
        public double CurrentAmps { get { return CurrentMilliamps / 1000.0; } }
        public double VoltageVolts { get { return VoltageMicrovolts / 1000000.0; } }
        public double SessionEnergyWh { get; set; }
    }

    internal sealed class PowerMonitorStatusContract
    {
        public PowerMonitorStatusContract()
        {
            Channels = new List<PowerMonitorChannelSample>();
        }

        public uint Mask { get; set; }
        public uint BoardPowerMilliwatts { get; set; }
        public double BoardPowerWatts { get { return BoardPowerMilliwatts / 1000.0; } }
        public double PrimarySessionEnergyWh { get; set; }
        public IList<PowerMonitorChannelSample> Channels { get; private set; }
    }

    internal sealed class PowerTopologyContract
    {
        public uint Count { get; set; }
        public double? ChipPowerWatts { get; set; }
        public double? BoardPowerWatts { get; set; }
    }

    internal sealed class PowerTelemetryContract
    {
        public PowerTelemetryContract()
        {
            Monitor = new PowerMonitorStatusContract();
            Topology = new PowerTopologyContract();
            PerfDecreaseReasons = new List<string>();
        }

        public PowerMonitorStatusContract Monitor { get; set; }
        public PowerTopologyContract Topology { get; set; }
        public uint? PerfDecreaseMask { get; set; }
        public bool? InsufficientExternalPower { get; set; }
        public IList<string> PerfDecreaseReasons { get; private set; }
    }

    internal static class NvApiPowerMonitorLayouts
    {
        private const int InfoEntriesOffset = 0x34;
        private const int InfoEntryStride = 0x3C;
        private const int StatusEntriesOffset = 0x1C;
        private const int StatusEntryStride = 0x2C;
        private const int MaximumChannels = 32;

        internal static byte[] CreateInfoRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.PowerMonitorInfoSizeV3];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.PowerMonitorInfoVersionV3);
            return buffer;
        }

        internal static PowerMonitorInfoContract ParseInfo(byte[] info)
        {
            RequireBuffer(
                info,
                PrivateNvApiContracts.PowerMonitorInfoSizeV3,
                PrivateNvApiContracts.PowerMonitorInfoVersionV3,
                "Power Monitor Info");

            bool supported = info[0x04] != 0;
            uint mask = NvApiTuningLayouts.ReadUInt32(info, 0x10);
            int primary = info[0x1C];
            if (!supported) throw new InvalidOperationException("Power Monitor Info 报告接口不可用。");
            if (mask == 0) throw new InvalidOperationException("Power Monitor Info mask 为空。");
            if (primary < 0 || primary >= MaximumChannels)
                throw new InvalidOperationException("Power Monitor 主通道索引越界。");

            PowerMonitorInfoContract result = new PowerMonitorInfoContract
            {
                Supported = supported,
                Mask = mask,
                PrimaryChannelIndex = primary
            };

            for (int channel = 0; channel < MaximumChannels; channel++)
            {
                if ((mask & (1U << channel)) == 0) continue;
                int entry = InfoEntriesOffset + channel * InfoEntryStride;
                uint railId = NvApiTuningLayouts.ReadUInt32(info, entry + 0x04);
                result.Channels.Add(new PowerMonitorChannelInfo
                {
                    ChannelIndex = channel,
                    InfoField0 = NvApiTuningLayouts.ReadUInt32(info, entry),
                    RailId = railId,
                    RailName = RailName(railId)
                });
            }

            if (result.FindChannel(primary) == null)
                throw new InvalidOperationException("Power Monitor 主通道不在 Info mask 中。");
            return result;
        }

        internal static byte[] CreateStatusRequest(PowerMonitorInfoContract info)
        {
            if (info == null) throw new ArgumentNullException("info");
            if (!info.Supported || info.Mask == 0)
                throw new InvalidOperationException("Power Monitor Info 不可用于 Status 请求。");
            byte[] buffer = new byte[PrivateNvApiContracts.PowerMonitorStatusSizeV1];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.PowerMonitorStatusVersionV1);
            NvApiTuningLayouts.WriteUInt32(buffer, 0x04, info.Mask);
            return buffer;
        }

        internal static ulong[] ReadEnergyCounters(PowerMonitorInfoContract info, byte[] status)
        {
            RequireStatus(info, status);
            ulong[] result = new ulong[MaximumChannels];
            for (int channel = 0; channel < MaximumChannels; channel++)
            {
                if ((info.Mask & (1U << channel)) == 0) continue;
                int entry = StatusEntriesOffset + channel * StatusEntryStride;
                result[channel] = ReadUInt64(status, entry + 0x14);
            }
            return result;
        }

        internal static PowerMonitorStatusContract ParseStatus(
            PowerMonitorInfoContract info,
            byte[] status,
            ulong[] energyBaseline)
        {
            RequireStatus(info, status);
            if (energyBaseline != null && energyBaseline.Length != MaximumChannels)
                throw new ArgumentException("Power Monitor 能量基线必须包含 32 个计数器。", "energyBaseline");

            PowerMonitorStatusContract result = new PowerMonitorStatusContract
            {
                Mask = info.Mask,
                BoardPowerMilliwatts = NvApiTuningLayouts.ReadUInt32(status, 0x08)
            };

            for (int listIndex = 0; listIndex < info.Channels.Count; listIndex++)
            {
                PowerMonitorChannelInfo channelInfo = info.Channels[listIndex];
                int entry = StatusEntriesOffset + channelInfo.ChannelIndex * StatusEntryStride;
                ulong energy = ReadUInt64(status, entry + 0x14);
                ulong delta = 0;
                if (energyBaseline != null && energy >= energyBaseline[channelInfo.ChannelIndex])
                    delta = energy - energyBaseline[channelInfo.ChannelIndex];

                PowerMonitorChannelSample sample = new PowerMonitorChannelSample
                {
                    ChannelIndex = channelInfo.ChannelIndex,
                    InfoField0 = channelInfo.InfoField0,
                    RailId = channelInfo.RailId,
                    RailName = channelInfo.RailName,
                    PowerMilliwatts = NvApiTuningLayouts.ReadUInt32(status, entry),
                    CurrentMilliamps = NvApiTuningLayouts.ReadUInt32(status, entry + 0x0C),
                    VoltageMicrovolts = NvApiTuningLayouts.ReadUInt32(status, entry + 0x10),
                    CumulativeEnergyMillijoules = energy,
                    SessionEnergyWh = delta / 3600000.0
                };
                result.Channels.Add(sample);
                if (sample.ChannelIndex == info.PrimaryChannelIndex)
                    result.PrimarySessionEnergyWh = sample.SessionEnergyWh;
            }
            return result;
        }

        internal static byte[] CreatePowerTopologyRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.PowerTopologyStatusSizeV1];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.PowerTopologyStatusVersionV1);
            return buffer;
        }

        internal static PowerTopologyContract ParsePowerTopology(byte[] status)
        {
            RequireBuffer(
                status,
                PrivateNvApiContracts.PowerTopologyStatusSizeV1,
                PrivateNvApiContracts.PowerTopologyStatusVersionV1,
                "Power Topology Status");
            uint count = NvApiTuningLayouts.ReadUInt32(status, 0x04);
            if (count > 4) throw new InvalidOperationException("Power Topology 条目数超过 4。");
            PowerTopologyContract result = new PowerTopologyContract { Count = count };
            for (int index = 0; index < count; index++)
            {
                int entry = 0x08 + index * 0x10;
                uint id = NvApiTuningLayouts.ReadUInt32(status, entry);
                double watts = NvApiTuningLayouts.ReadUInt32(status, entry + 0x08) / 1000.0;
                if (id == 0) result.ChipPowerWatts = watts;
                else if (id == 1) result.BoardPowerWatts = watts;
            }
            return result;
        }

        internal static IList<string> DecodePerfDecreaseReasons(uint mask)
        {
            List<string> result = new List<string>();
            if ((mask & 0x00000001U) != 0) result.Add("THERMAL_PROTECTION");
            if ((mask & 0x00000002U) != 0) result.Add("POWER_CONTROL");
            if ((mask & 0x00000004U) != 0) result.Add("AC_BATT");
            if ((mask & 0x00000008U) != 0) result.Add("API_TRIGGERED");
            if ((mask & 0x00000010U) != 0) result.Add("INSUFFICIENT_POWER");
            if ((mask & 0x80000000U) != 0) result.Add("UNKNOWN");
            return result;
        }

        internal static string RailName(uint railId)
        {
            switch (railId)
            {
                case 1: return "NVVDD output";
                case 2: return "FBVDD output";
                case 3: return "FBVDDQ output";
                case 8: return "Total GPU output";
                case 11: return "SRAM output";
                case 16: return "MSVDD";
                case 232: return "Misc0 input";
                case 245: return "Total board";
                case 246: return "NVVDD input";
                case 247: return "FBVDD input";
                case 248: return "FBVDDQ input";
                case 250: return "8-pin input 0";
                case 251: return "8-pin input 1";
                case 254: return "PCIe 3.3V";
                case 255: return "PCIe 12V";
                default: return "Rail " + railId;
            }
        }

        private static void RequireStatus(PowerMonitorInfoContract info, byte[] status)
        {
            if (info == null) throw new ArgumentNullException("info");
            RequireBuffer(
                status,
                PrivateNvApiContracts.PowerMonitorStatusSizeV1,
                PrivateNvApiContracts.PowerMonitorStatusVersionV1,
                "Power Monitor Status");
            uint statusMask = NvApiTuningLayouts.ReadUInt32(status, 0x04);
            if (statusMask != info.Mask)
                throw new InvalidOperationException("Power Monitor Status mask 与 Info 不一致。");
        }

        private static ulong ReadUInt64(byte[] buffer, int offset)
        {
            if (offset < 0 || offset + 8 > buffer.Length)
                throw new ArgumentOutOfRangeException("offset");
            return BitConverter.ToUInt64(buffer, offset);
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
