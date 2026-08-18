using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed class AdcDeviceContract
    {
        public int DeviceIndex { get; set; }
        public uint DomainId { get; set; }
        public string DomainName { get; set; }
        public uint InfoField8 { get; set; }
        public uint InfoFieldC { get; set; }
        public short FuseOffset { get; set; }
        public short FuseGain { get; set; }
        public bool RawValid { get; set; }
        public uint RawValue { get; set; }
        public uint CorrectedVoltageUv { get; set; }
        public byte StatusByte8 { get; set; }
        public byte StatusByte9 { get; set; }
        public byte StatusByteA { get; set; }
        public byte StatusByte2C { get; set; }
    }

    internal sealed class AdcDevicesContract
    {
        public AdcDevicesContract()
        {
            Devices = new List<AdcDeviceContract>();
        }

        public uint Mask { get; set; }
        public uint InfoField18 { get; set; }
        public uint InfoField1C { get; set; }
        public IList<AdcDeviceContract> Devices { get; private set; }
    }

    internal static class NvApiAdcLayouts
    {
        private const int InfoEntriesOffset = 0x70;
        private const int StatusEntriesOffset = 0x48;
        private const int EntryStride = 0x4C;

        private static readonly string[] DomainNames = new string[]
        {
            "SYS", "LTC", "XBAR", "GPC0", "GPC1", "GPC2", "GPC3",
            "GPC4", "GPC5", "GPCS", "SRAM", "NVD", "HOST", "GPC6",
            "GPC7", "GPC8", "GPC9", "GPC10", "GPC11", "SYS ISINK"
        };

        internal static byte[] CreateInfoRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.AdcDevicesInfoSizeV2];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.AdcDevicesInfoVersionV2);
            return buffer;
        }

        internal static byte[] CreateStatusRequest(byte[] info)
        {
            RequireInfo(info);
            uint mask = NvApiTuningLayouts.ReadUInt32(info, 0x04);
            if (mask == 0) throw new InvalidOperationException("ADC Info mask 为空。");
            byte[] buffer = new byte[PrivateNvApiContracts.AdcDevicesStatusSizeV1];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.AdcDevicesStatusVersionV1);
            NvApiTuningLayouts.WriteUInt32(buffer, 0x04, mask);
            return buffer;
        }

        internal static AdcDevicesContract Parse(byte[] info, byte[] status)
        {
            RequireInfo(info);
            RequireStatus(status);
            uint mask = NvApiTuningLayouts.ReadUInt32(info, 0x04);
            if (mask == 0) throw new InvalidOperationException("ADC Info mask 为空。");
            if (NvApiTuningLayouts.ReadUInt32(status, 0x04) != mask)
                throw new InvalidOperationException("ADC Status mask 与 Info 不一致。");

            AdcDevicesContract result = new AdcDevicesContract
            {
                Mask = mask,
                InfoField18 = NvApiTuningLayouts.ReadUInt32(info, 0x18),
                InfoField1C = NvApiTuningLayouts.ReadUInt32(info, 0x1C)
            };

            for (int device = 0; device < 32; device++)
            {
                if ((mask & (1U << device)) == 0) continue;
                int infoEntry = InfoEntriesOffset + device * EntryStride;
                int statusEntry = StatusEntriesOffset + device * EntryStride;
                if (infoEntry + 0x2E > info.Length || statusEntry + 0x2D > status.Length)
                    throw new InvalidOperationException("ADC 固定索引条目越界。");
                if (NvApiTuningLayouts.ReadUInt32(info, infoEntry) != 3U)
                    continue;

                uint domain = NvApiTuningLayouts.ReadUInt32(info, infoEntry + 0x04);
                uint raw = NvApiTuningLayouts.ReadUInt32(status, statusEntry);
                uint corrected = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x04);
                if (corrected > 2000000U)
                    throw new InvalidOperationException("ADC device " + device + " 的校正电压超过 2000000 µV。");

                result.Devices.Add(new AdcDeviceContract
                {
                    DeviceIndex = device,
                    DomainId = domain,
                    DomainName = DomainName(domain),
                    InfoField8 = NvApiTuningLayouts.ReadUInt32(info, infoEntry + 0x08),
                    InfoFieldC = NvApiTuningLayouts.ReadUInt32(info, infoEntry + 0x0C),
                    FuseOffset = DecodeSignedMagnitude7(info[infoEntry + 0x2C]),
                    FuseGain = DecodeSignedMagnitude7(info[infoEntry + 0x2D]),
                    RawValid = raw != UInt32.MaxValue,
                    RawValue = raw,
                    CorrectedVoltageUv = corrected,
                    StatusByte8 = status[statusEntry + 0x08],
                    StatusByte9 = status[statusEntry + 0x09],
                    StatusByteA = status[statusEntry + 0x0A],
                    StatusByte2C = status[statusEntry + 0x2C]
                });
            }

            if (result.Devices.Count == 0)
                throw new InvalidOperationException("ADC Info 没有类型 3 的 V30 设备。");
            return result;
        }

        internal static short DecodeSignedMagnitude7(byte encoded)
        {
            int magnitude = encoded & 0x7F;
            return (short)((encoded & 0x80) == 0 ? magnitude : -magnitude);
        }

        internal static string DomainName(uint domainId)
        {
            return domainId < DomainNames.Length ? DomainNames[domainId] : "DOMAIN_" + domainId;
        }

        private static void RequireInfo(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.AdcDevicesInfoSizeV2, PrivateNvApiContracts.AdcDevicesInfoVersionV2, "ADC Info");
        }

        private static void RequireStatus(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.AdcDevicesStatusSizeV1, PrivateNvApiContracts.AdcDevicesStatusVersionV1, "ADC Status");
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
