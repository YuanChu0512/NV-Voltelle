using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed class VoltageRailContract
    {
        public int RailIndex { get; set; }
        public int PackedIndex { get; set; }
        public int Type { get; set; }

        // Six signed fields copied by mVolt from Control entry +0x04..+0x18.
        public int PrimaryMaximumOffsetUv { get; set; }
        public int AlternateMaximumOffsetUv { get; set; }
        public int ControlField3Uv { get; set; }
        public int MinimumOffsetUv { get; set; }
        public int ControlField5Uv { get; set; }
        public int ControlField6Uv { get; set; }

        // Names are proven by mVolt's dashboard renderer at 0x140020F40.
        public uint SensedUv { get; set; }
        public uint ReliabilityLimitUv { get; set; }
        public uint AlternateLimitUv { get; set; }
        public uint OvervoltageLimitUv { get; set; }
        public uint MaximumLimitUv { get; set; }
        public uint MinimumLimitUv { get; set; }
        public int MarginUv { get; set; }
        public uint NoiseLimitUv { get; set; }

        // Present in the v2 Status entry but not labelled by the dashboard path.
        public uint StatusField9 { get; set; }
        public byte StatusByte10 { get; set; }
        public bool StatusFlag11 { get; set; }
        public uint StatusField12 { get; set; }
        public uint StatusField13 { get; set; }
        public uint StatusField14 { get; set; }
    }

    internal sealed class VoltageRailsContract
    {
        public VoltageRailsContract()
        {
            Rails = new List<VoltageRailContract>();
        }

        public uint Mask { get; set; }
        public byte VoltageBoostPercent { get; set; }
        public IList<VoltageRailContract> Rails { get; private set; }

        public VoltageRailContract FindRail(int railIndex)
        {
            for (int index = 0; index < Rails.Count; index++)
                if (Rails[index].RailIndex == railIndex) return Rails[index];
            return null;
        }
    }

    internal sealed class VoltageRailTargetOffsets
    {
        public int PrimaryMaximumOffsetUv { get; set; }
        public int AlternateMaximumOffsetUv { get; set; }
        public int MinimumOffsetUv { get; set; }
        public uint ExpectedReliabilityLimitUv { get; set; }
        public uint ExpectedAlternateLimitUv { get; set; }
        public uint ExpectedMinimumLimitUv { get; set; }
        public uint VoltageCapUv { get; set; }
        public bool AlternateLimitMayRemainZero { get; set; }
    }

    internal static class NvApiVoltageLayouts
    {
        private const int InfoMaskOffset = 0x04;
        private const int InfoRailTypeOffset = 0x4C;
        private const int InfoRailStride = 0xC0;
        private const int ControlBoostOffset = 0x08;
        private const int ControlEntriesOffset = 0x48;
        private const int ControlEntryStride = 0x54;
        private const int StatusEntriesOffset = 0xA0;
        private const int StatusEntryStride = 0xAC;

        internal static byte[] CreateInfoRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.VoltRailsInfoSizeV2];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.VoltRailsInfoVersionV2);
            return buffer;
        }

        internal static byte[] CreateControlRequest(byte[] info)
        {
            RequireInfo(info);
            uint mask = NvApiTuningLayouts.ReadUInt32(info, InfoMaskOffset);
            if (mask == 0) throw new InvalidOperationException("VoltRails Info mask 为空。");

            byte[] buffer = new byte[PrivateNvApiContracts.VoltRailsControlSizeV2];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.VoltRailsControlVersionV2);
            NvApiTuningLayouts.WriteUInt32(buffer, InfoMaskOffset, mask);

            int packed = 0;
            for (int rail = 0; rail < 32; rail++)
            {
                if ((mask & (1U << rail)) == 0) continue;
                int source = InfoRailTypeOffset + rail * InfoRailStride;
                int destination = ControlEntriesOffset + packed * ControlEntryStride;
                if (source + 4 > info.Length || destination + 4 > buffer.Length)
                    throw new InvalidOperationException("VoltRails Control 类型种子越界。");
                NvApiTuningLayouts.WriteUInt32(buffer, destination, NvApiTuningLayouts.ReadUInt32(info, source));
                packed++;
            }
            return buffer;
        }

        internal static byte[] CreateStatusRequest(byte[] info)
        {
            RequireInfo(info);
            uint mask = NvApiTuningLayouts.ReadUInt32(info, InfoMaskOffset);
            if (mask == 0) throw new InvalidOperationException("VoltRails Info mask 为空。");
            byte[] buffer = new byte[PrivateNvApiContracts.VoltRailsStatusSizeV2];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, PrivateNvApiContracts.VoltRailsStatusVersionV2);
            NvApiTuningLayouts.WriteUInt32(buffer, InfoMaskOffset, mask);
            return buffer;
        }

        internal static VoltageRailsContract Parse(byte[] info, byte[] control, byte[] status)
        {
            RequireInfo(info);
            RequireControl(control);
            RequireStatus(status);

            uint mask = NvApiTuningLayouts.ReadUInt32(info, InfoMaskOffset);
            if ((mask & 1U) == 0)
                throw new InvalidOperationException("VoltRails mask 未包含 rail 0。");
            RequireEqualMask(mask, control, "Control");
            RequireEqualMask(mask, status, "Status");

            byte boost = control[ControlBoostOffset];
            if (boost > 100)
                throw new InvalidOperationException("Voltage Boost 百分比超出 0..100。");

            VoltageRailsContract result = new VoltageRailsContract
            {
                Mask = mask,
                VoltageBoostPercent = boost
            };

            int packed = 0;
            for (int rail = 0; rail < 32; rail++)
            {
                if ((mask & (1U << rail)) == 0) continue;
                int controlEntry = ControlEntriesOffset + packed * ControlEntryStride;
                int statusEntry = StatusEntriesOffset + packed * StatusEntryStride;
                if (controlEntry + 0x1C > control.Length || statusEntry + 0x3C > status.Length)
                    throw new InvalidOperationException("VoltRails 紧凑条目越界。");

                VoltageRailContract item = new VoltageRailContract
                {
                    RailIndex = rail,
                    PackedIndex = packed,
                    Type = NvApiTuningLayouts.ReadInt32(control, controlEntry),
                    PrimaryMaximumOffsetUv = NvApiTuningLayouts.ReadInt32(control, controlEntry + 0x04),
                    AlternateMaximumOffsetUv = NvApiTuningLayouts.ReadInt32(control, controlEntry + 0x08),
                    ControlField3Uv = NvApiTuningLayouts.ReadInt32(control, controlEntry + 0x0C),
                    MinimumOffsetUv = NvApiTuningLayouts.ReadInt32(control, controlEntry + 0x10),
                    ControlField5Uv = NvApiTuningLayouts.ReadInt32(control, controlEntry + 0x14),
                    ControlField6Uv = NvApiTuningLayouts.ReadInt32(control, controlEntry + 0x18),
                    SensedUv = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x04),
                    ReliabilityLimitUv = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x08),
                    AlternateLimitUv = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x0C),
                    OvervoltageLimitUv = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x10),
                    MaximumLimitUv = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x14),
                    MinimumLimitUv = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x18),
                    MarginUv = NvApiTuningLayouts.ReadInt32(status, statusEntry + 0x1C),
                    NoiseLimitUv = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x20),
                    StatusField9 = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x24),
                    StatusByte10 = status[statusEntry + 0x28],
                    StatusFlag11 = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x2C) != 0,
                    StatusField12 = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x30),
                    StatusField13 = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x34),
                    StatusField14 = NvApiTuningLayouts.ReadUInt32(status, statusEntry + 0x38)
                };
                ValidateRail(item);
                result.Rails.Add(item);
                packed++;
            }

            if (result.FindRail(0) == null || result.FindRail(1) == null)
                throw new InvalidOperationException("mVolt 契约要求 rail 0 和 rail 1 同时存在。");
            return result;
        }

        internal static byte[] CreateBoostSet(byte[] currentControl, int percentage)
        {
            RequireControl(currentControl);
            if (percentage < 0 || percentage > 100)
                throw new ArgumentOutOfRangeException("percentage", "Voltage Boost 超出 0..100%。");
            byte[] result = (byte[])currentControl.Clone();
            result[ControlBoostOffset] = (byte)percentage;
            return result;
        }

        internal static byte[] CreateResetSet(byte[] currentControl, VoltageRailsContract current)
        {
            RequireControl(currentControl);
            if (current == null) throw new ArgumentNullException("current");
            uint mask = NvApiTuningLayouts.ReadUInt32(currentControl, InfoMaskOffset);
            if (mask == 0 || mask != current.Mask)
                throw new InvalidOperationException("VoltRails reset mask 与当前契约不一致。");

            byte[] result = (byte[])currentControl.Clone();
            result[ControlBoostOffset] = 0;
            for (int index = 0; index < current.Rails.Count; index++)
            {
                VoltageRailContract rail = current.Rails[index];
                if (rail == null || rail.RailIndex < 0 || rail.RailIndex >= 32 ||
                    (mask & (1U << rail.RailIndex)) == 0)
                    throw new InvalidOperationException("VoltRails reset 包含无效 rail。");
                int packed = CountBitsBefore(mask, rail.RailIndex);
                int entry = ControlEntriesOffset + packed * ControlEntryStride;
                if (entry + 0x14 > result.Length || NvApiTuningLayouts.ReadInt32(result, entry) != rail.Type)
                    throw new InvalidOperationException("VoltRails reset rail 布局与当前契约不一致。");
                NvApiTuningLayouts.WriteInt32(result, entry + 0x04, 0);
                NvApiTuningLayouts.WriteInt32(result, entry + 0x08, 0);
                NvApiTuningLayouts.WriteInt32(result, entry + 0x10, 0);
            }
            return result;
        }

        internal static VoltageRailTargetOffsets CalculateTargetOffsets(
            VoltageRailContract rail,
            int targetMinimumMv,
            int targetMaximumMv,
            bool allow1250Mv,
            bool mobileRelOnly)
        {
            if (rail == null) throw new ArgumentNullException("rail");
            int capMv = allow1250Mv ? 1250 : 1150;
            if (targetMinimumMv < 250 || targetMaximumMv > capMv || targetMinimumMv > targetMaximumMv)
                throw new ArgumentOutOfRangeException("targetMinimumMv", "电压范围必须满足 250 <= MIN <= MAX <= " + capMv + " mV。");
            if (targetMinimumMv % 5 != 0 || targetMaximumMv % 5 != 0)
                throw new ArgumentException("MIN 和 MAX 必须是 5 mV 的整数倍。");

            long baseReliability = (long)rail.ReliabilityLimitUv - rail.PrimaryMaximumOffsetUv;
            long baseAlternate = rail.AlternateLimitUv == 0
                ? 0
                : (long)rail.AlternateLimitUv - rail.AlternateMaximumOffsetUv;
            long baseMinimum = (long)rail.MinimumLimitUv - rail.MinimumOffsetUv;
            if (baseReliability % 1000 != 0)
                throw new InvalidOperationException("REL 减主 offset 后不是整数 mV。");

            long targetMaximumUv = checked((long)targetMaximumMv * 1000L);
            long targetMinimumUv = checked((long)targetMinimumMv * 1000L);
            long primary = targetMaximumUv - baseReliability;
            long alternate;
            if (primary <= 0)
            {
                alternate = 0;
            }
            else if (mobileRelOnly && rail.AlternateLimitUv == 0)
            {
                // Exact special branch at 0x140016D9D: when the alternate path
                // is absent on the original REL-only GPU path, the high DWORD
                // remains zero even for a positive primary target.
                alternate = 0;
            }
            else
            {
                alternate = primary + rail.AlternateMaximumOffsetUv - rail.AlternateLimitUv + baseReliability;
            }
            long minimum = targetMinimumUv - baseMinimum;

            VoltageRailTargetOffsets result = new VoltageRailTargetOffsets
            {
                PrimaryMaximumOffsetUv = CheckedInt(primary, "主 MAX offset"),
                AlternateMaximumOffsetUv = CheckedInt(alternate, "ALT offset"),
                MinimumOffsetUv = CheckedInt(minimum, "MIN offset"),
                ExpectedReliabilityLimitUv = CheckedUInt(baseReliability + primary, "预期 REL"),
                ExpectedAlternateLimitUv = CheckedUInt(baseAlternate == 0 ? 0 : baseAlternate + alternate, "预期 ALT"),
                ExpectedMinimumLimitUv = CheckedUInt(baseMinimum + minimum, "预期 MIN"),
                VoltageCapUv = (uint)(capMv * 1000),
                AlternateLimitMayRemainZero = mobileRelOnly && rail.AlternateLimitUv == 0
            };
            ValidateTarget(result);
            return result;
        }

        internal static bool IsMobileRelOnlyGpu(string gpuName)
        {
            if (String.IsNullOrEmpty(gpuName)) return false;
            // REL-only control is limited to these mobile/workstation
            // Blackwell product-name families.
            return gpuName.IndexOf("GeForce RTX 5090 Laptop GPU", StringComparison.Ordinal) >= 0 ||
                gpuName.IndexOf("RTX PRO 6000 Blackwell Workstation Edition", StringComparison.Ordinal) >= 0;
        }

        internal static byte[] CreateRailSet(byte[] currentControl, VoltageRailContract rail, VoltageRailTargetOffsets target)
        {
            RequireControl(currentControl);
            if (rail == null) throw new ArgumentNullException("rail");
            if (target == null) throw new ArgumentNullException("target");
            ValidateTarget(target);

            uint mask = NvApiTuningLayouts.ReadUInt32(currentControl, InfoMaskOffset);
            if (rail.RailIndex < 0 || rail.RailIndex >= 32 || (mask & (1U << rail.RailIndex)) == 0)
                throw new ArgumentOutOfRangeException("rail", "目标电压轨不在 Control mask 中。");
            int packed = CountBitsBefore(mask, rail.RailIndex);
            int entry = ControlEntriesOffset + packed * ControlEntryStride;
            if (entry + 0x14 > currentControl.Length)
                throw new InvalidOperationException("VoltRails Control 目标条目越界。");
            if (NvApiTuningLayouts.ReadInt32(currentControl, entry) != 3)
                throw new InvalidOperationException("VoltRails Control 目标条目类型不是 3。");

            byte[] result = (byte[])currentControl.Clone();
            NvApiTuningLayouts.WriteInt32(result, entry + 0x04, target.PrimaryMaximumOffsetUv);
            NvApiTuningLayouts.WriteInt32(result, entry + 0x08, target.AlternateMaximumOffsetUv);
            NvApiTuningLayouts.WriteInt32(result, entry + 0x10, target.MinimumOffsetUv);
            return result;
        }

        internal static void ValidateRailReadBack(VoltageRailContract rail, VoltageRailTargetOffsets target)
        {
            if (rail == null) throw new ArgumentNullException("rail");
            if (target == null) throw new ArgumentNullException("target");
            if (rail.PrimaryMaximumOffsetUv != target.PrimaryMaximumOffsetUv ||
                rail.AlternateMaximumOffsetUv != target.AlternateMaximumOffsetUv ||
                rail.MinimumOffsetUv != target.MinimumOffsetUv)
                throw new InvalidOperationException("VoltRails Control 回读与请求不一致。");
            if (rail.ReliabilityLimitUv != target.ExpectedReliabilityLimitUv ||
                rail.MaximumLimitUv != target.ExpectedReliabilityLimitUv ||
                rail.MinimumLimitUv != target.ExpectedMinimumLimitUv)
                throw new InvalidOperationException("VoltRails Status REL/MAX/MIN 回读与请求不一致。");
            if (target.AlternateLimitMayRemainZero)
            {
                if (rail.AlternateLimitUv != 0)
                    throw new InvalidOperationException("VoltRails ALT 应保持为 0。");
            }
            else if (target.ExpectedAlternateLimitUv != 0 && rail.AlternateLimitUv != target.ExpectedAlternateLimitUv)
            {
                throw new InvalidOperationException("VoltRails ALT 回读与请求不一致。");
            }
            if (rail.MaximumLimitUv > target.VoltageCapUv)
                throw new InvalidOperationException("VoltRails MAX 回读超过电压上限。");
        }

        private static void ValidateRail(VoltageRailContract rail)
        {
            if (rail.Type != 3)
                throw new InvalidOperationException("VoltRails rail " + rail.RailIndex + " 的 Control 类型不是 3。");
            ValidateSigned(rail.PrimaryMaximumOffsetUv, 1000000, "primary MAX offset");
            ValidateSigned(rail.AlternateMaximumOffsetUv, 250000, "ALT offset");
            ValidateSigned(rail.ControlField3Uv, 250000, "control field 3");
            ValidateSigned(rail.MinimumOffsetUv, 1000000, "MIN offset");
            ValidateSigned(rail.ControlField5Uv, 250000, "control field 5");
            ValidateSigned(rail.ControlField6Uv, 250000, "control field 6");
            ValidateVoltage(rail.ReliabilityLimitUv, false, "REL");
            ValidateVoltage(rail.AlternateLimitUv, true, "ALT");
            ValidateVoltage(rail.OvervoltageLimitUv, false, "OV");
            ValidateVoltage(rail.MaximumLimitUv, false, "MAX");
            ValidateVoltage(rail.MinimumLimitUv, false, "MIN");
        }

        private static void ValidateTarget(VoltageRailTargetOffsets target)
        {
            ValidateSigned(target.PrimaryMaximumOffsetUv, 1000000, "主 MAX offset");
            if (target.PrimaryMaximumOffsetUv > 250000)
                throw new ArgumentOutOfRangeException("target", "主 MAX offset 超出 +250000 uV 上限。");
            ValidateSigned(target.AlternateMaximumOffsetUv, 250000, "ALT offset");
            ValidateSigned(target.MinimumOffsetUv, 1000000, "MIN offset");
            if (target.ExpectedReliabilityLimitUv < 250000 || target.ExpectedReliabilityLimitUv > target.VoltageCapUv)
                throw new ArgumentOutOfRangeException("target", "预期 REL 超出允许范围。");
            if (target.ExpectedMinimumLimitUv < 250000 || target.ExpectedMinimumLimitUv > target.ExpectedReliabilityLimitUv)
                throw new ArgumentOutOfRangeException("target", "预期 MIN 超出允许范围。");
            if (target.ExpectedAlternateLimitUv != 0 && target.ExpectedAlternateLimitUv > target.VoltageCapUv)
                throw new ArgumentOutOfRangeException("target", "预期 ALT 超出允许范围。");
        }

        private static void ValidateSigned(int value, int magnitude, string field)
        {
            if (value < -magnitude || value > magnitude)
                throw new InvalidOperationException("VoltRails " + field + " 超出 ±" + magnitude + " uV。");
        }

        private static void ValidateVoltage(uint value, bool allowZero, string field)
        {
            if (allowZero && value == 0) return;
            if (value < 250000 || value > 2000000)
                throw new InvalidOperationException("VoltRails " + field + " 超出 250000..2000000 uV。");
        }

        private static int CountBitsBefore(uint mask, int railIndex)
        {
            uint before = railIndex == 0 ? 0U : mask & ((1U << railIndex) - 1U);
            int count = 0;
            while (before != 0)
            {
                before &= before - 1;
                count++;
            }
            return count;
        }

        private static int CheckedInt(long value, string field)
        {
            if (value < Int32.MinValue || value > Int32.MaxValue)
                throw new OverflowException(field + " 超出 Int32。");
            return (int)value;
        }

        private static uint CheckedUInt(long value, string field)
        {
            if (value < UInt32.MinValue || value > UInt32.MaxValue)
                throw new OverflowException(field + " 超出 UInt32。");
            return (uint)value;
        }

        private static void RequireEqualMask(uint expected, byte[] buffer, string name)
        {
            uint actual = NvApiTuningLayouts.ReadUInt32(buffer, InfoMaskOffset);
            if (actual != expected)
                throw new InvalidOperationException("VoltRails " + name + " mask 与 Info 不一致。");
        }

        private static void RequireInfo(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.VoltRailsInfoSizeV2, PrivateNvApiContracts.VoltRailsInfoVersionV2, "VoltRails Info");
        }

        private static void RequireControl(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.VoltRailsControlSizeV2, PrivateNvApiContracts.VoltRailsControlVersionV2, "VoltRails Control");
        }

        private static void RequireStatus(byte[] buffer)
        {
            RequireBuffer(buffer, PrivateNvApiContracts.VoltRailsStatusSizeV2, PrivateNvApiContracts.VoltRailsStatusVersionV2, "VoltRails Status");
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
