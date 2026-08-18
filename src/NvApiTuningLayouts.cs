using System;

namespace MVolt.Rebuild
{
    internal sealed class PstateClockContract
    {
        public int DomainId { get; set; }
        public int TypeId { get; set; }
        public bool GlobalEditable { get; set; }
        public bool Editable { get; set; }
        public int CurrentOffsetKHz { get; set; }
        public int MinimumOffsetKHz { get; set; }
        public int MaximumOffsetKHz { get; set; }
    }

    internal sealed class PowerPolicyContract
    {
        public int PolicyId { get; set; }
        public uint CurrentRaw { get; set; }
        public uint MinimumRaw { get; set; }
        public uint DefaultRaw { get; set; }
        public uint MaximumRaw { get; set; }
    }

    internal sealed class BoostLockContract
    {
        public bool Enabled { get; set; }
        public bool VoltageLockPresent { get; set; }
    }

    internal static class NvApiTuningLayouts
    {
        internal const int CoreDomain = 0;
        internal const int MemoryDomain = 4;
        internal const int BoostLockDomain = 6;

        private const int PstateCountMaximum = 16;
        private const int ClockCountMaximum = 8;
        private const int PstateArrayOffset = 0x14;
        private const int PstateStride = 0x1C8;
        private const int ClockArrayOffset = 0x08;
        private const int ClockStride = 0x2C;

        private const int PowerEntryMaximum = 4;
        private const int PowerInfoEntriesOffset = 0x08;
        private const int PowerInfoEntryStride = 0x2C;
        private const int PowerStatusEntriesOffset = 0x08;
        private const int PowerStatusEntryStride = 0x10;

        private const int BoostEntryCount = 7;
        private const int BoostEntriesOffset = 0x0C;
        private const int BoostEntryStride = 0x18;
        private const uint BoostLockSentinelUv = 1500000;

        internal static byte[] CreatePstatesRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.PstatesSizeV2];
            WriteUInt32(buffer, 0, PrivateNvApiContracts.PstatesVersionV2);
            return buffer;
        }

        internal static byte[] CreatePstateSet(int domainId, int offsetMHz)
        {
            return CreatePstateSetKHz(domainId, ToKHz(offsetMHz, "offsetMHz"));
        }

        internal static byte[] CreatePstateSetKHz(int domainId, int offsetKHz)
        {
            if (domainId != CoreDomain && domainId != MemoryDomain)
                throw new ArgumentOutOfRangeException("domainId", "mVolt 只对核心域 0 和显存域 4 写入 P0 offset。");

            byte[] buffer = CreatePstatesRequest();

            // Pstates20 v2 single-domain SET layout.
            // Global bIsEditable and clock typeId remain zero.
            WriteInt32(buffer, 0x08, 1); // numPstates
            WriteInt32(buffer, 0x0C, 1); // numClocks
            WriteInt32(buffer, 0x14, 0); // P0
            WriteInt32(buffer, 0x18, 1); // pstate bIsEditable
            WriteInt32(buffer, 0x1C, domainId);
            WriteInt32(buffer, 0x24, 1); // clock bIsEditable
            WriteInt32(buffer, 0x28, offsetKHz);
            return buffer;
        }

        internal static PstateClockContract ParsePstateClock(byte[] buffer, int domainId)
        {
            RequireLength(buffer, PrivateNvApiContracts.PstatesSizeV2, "Pstates20");
            RequireVersion(buffer, PrivateNvApiContracts.PstatesVersionV2, "Pstates20");

            int pstateCount = ReadBoundedCount(buffer, 0x08, PstateCountMaximum, "Pstates20.numPstates");
            int clockCount = ReadBoundedCount(buffer, 0x0C, ClockCountMaximum, "Pstates20.numClocks");
            bool globalEditable = (ReadUInt32(buffer, 0x04) & 1U) != 0;

            for (int pstate = 0; pstate < pstateCount; pstate++)
            {
                int pstateOffset = PstateArrayOffset + pstate * PstateStride;
                if (ReadInt32(buffer, pstateOffset) != 0) continue;

                for (int clock = 0; clock < clockCount; clock++)
                {
                    int entry = pstateOffset + ClockArrayOffset + clock * ClockStride;
                    if (ReadInt32(buffer, entry) != domainId) continue;
                    return new PstateClockContract
                    {
                        DomainId = domainId,
                        TypeId = ReadInt32(buffer, entry + 0x04),
                        GlobalEditable = globalEditable,
                        Editable = (ReadUInt32(buffer, entry + 0x08) & 1U) != 0,
                        CurrentOffsetKHz = ReadInt32(buffer, entry + 0x0C),
                        MinimumOffsetKHz = ReadInt32(buffer, entry + 0x10),
                        MaximumOffsetKHz = ReadInt32(buffer, entry + 0x14)
                    };
                }
            }

            throw new InvalidOperationException("Pstates20 未返回 P0 domain " + domainId + "。");
        }

        internal static byte[] CreatePowerInfoRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.PowerInfoSizeV1];
            WriteUInt32(buffer, 0, PrivateNvApiContracts.PowerInfoVersionV1);
            return buffer;
        }

        internal static byte[] CreatePowerStatusRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.PowerStatusSizeV1];
            WriteUInt32(buffer, 0, PrivateNvApiContracts.PowerStatusVersionV1);
            return buffer;
        }

        internal static PowerPolicyContract ParsePowerPolicy(byte[] info, byte[] status, int policyId)
        {
            RequireLength(info, PrivateNvApiContracts.PowerInfoSizeV1, "PowerInfo");
            RequireVersion(info, PrivateNvApiContracts.PowerInfoVersionV1, "PowerInfo");
            RequireLength(status, PrivateNvApiContracts.PowerStatusSizeV1, "PowerStatus");
            RequireVersion(status, PrivateNvApiContracts.PowerStatusVersionV1, "PowerStatus");

            if (info[4] == 0)
                throw new InvalidOperationException("PowerInfo 标记为无效。");
            int infoCount = info[5];
            if (infoCount < 1 || infoCount > PowerEntryMaximum)
                throw new InvalidOperationException("PowerInfo.count 超出 1..4：" + infoCount);

            int statusCount = ReadBoundedCount(status, 0x04, PowerEntryMaximum, "PowerStatus.count");
            if (statusCount == 0)
                throw new InvalidOperationException("PowerStatus 没有条目。");

            int infoEntry = FindEntry(info, PowerInfoEntriesOffset, PowerInfoEntryStride, infoCount, policyId);
            int statusEntry = FindEntry(status, PowerStatusEntriesOffset, PowerStatusEntryStride, statusCount, policyId);
            if (infoEntry < 0 || statusEntry < 0)
                throw new InvalidOperationException("功耗策略 " + policyId + " 不同时存在于 Info 和 Status。");

            return new PowerPolicyContract
            {
                PolicyId = policyId,
                MinimumRaw = ReadUInt32(info, infoEntry + 0x0C),
                DefaultRaw = ReadUInt32(info, infoEntry + 0x18),
                MaximumRaw = ReadUInt32(info, infoEntry + 0x24),
                CurrentRaw = ReadUInt32(status, statusEntry + 0x08)
            };
        }

        internal static byte[] CreatePowerSet(byte[] currentStatus, int policyId, int powerPercent)
        {
            return CreatePowerSetRaw(currentStatus, policyId, ToPowerRaw(powerPercent));
        }

        internal static byte[] CreatePowerSetRaw(byte[] currentStatus, int policyId, uint powerRaw)
        {
            RequireLength(currentStatus, PrivateNvApiContracts.PowerStatusSizeV1, "PowerStatus");
            RequireVersion(currentStatus, PrivateNvApiContracts.PowerStatusVersionV1, "PowerStatus");
            int count = ReadBoundedCount(currentStatus, 0x04, PowerEntryMaximum, "PowerStatus.count");
            int entry = FindEntry(currentStatus, PowerStatusEntriesOffset, PowerStatusEntryStride, count, policyId);
            if (entry < 0)
                throw new InvalidOperationException("PowerStatus 不包含策略 " + policyId + "。");

            byte[] result = (byte[])currentStatus.Clone();
            WriteUInt32(result, entry + 0x08, powerRaw);
            return result;
        }

        internal static byte[] CreateBoostStatusRequest()
        {
            byte[] buffer = new byte[PrivateNvApiContracts.BoostLockSizeV2];
            WriteUInt32(buffer, 0, PrivateNvApiContracts.BoostLockVersionV2);
            return buffer;
        }

        internal static byte[] CreateBoostSet(bool enabled)
        {
            byte[] buffer = CreateBoostStatusRequest();
            WriteInt32(buffer, 0x08, 1);
            WriteInt32(buffer, 0x0C, BoostLockDomain);
            WriteInt32(buffer, 0x14, enabled ? 3 : 0);
            WriteUInt32(buffer, 0x1C, enabled ? BoostLockSentinelUv : 0U);
            return buffer;
        }

        internal static BoostLockContract ParseBoostStatus(byte[] buffer)
        {
            RequireLength(buffer, PrivateNvApiContracts.BoostLockSizeV2, "PerfClientLimits");
            RequireVersion(buffer, PrivateNvApiContracts.BoostLockVersionV2, "PerfClientLimits");
            if (ReadUInt32(buffer, 0x04) != 0)
                throw new InvalidOperationException("PerfClientLimits.flags 必须为 0。");
            if (ReadInt32(buffer, 0x08) != BoostEntryCount)
                throw new InvalidOperationException("PerfClientLimits.count 不是 mVolt 预期的 7。");

            bool boostLock = false;
            bool voltageLock = false;
            for (int index = 0; index < BoostEntryCount; index++)
            {
                int entry = BoostEntriesOffset + index * BoostEntryStride;
                uint id = ReadUInt32(buffer, entry);
                uint reserved1 = ReadUInt32(buffer, entry + 0x04);
                uint mode = ReadUInt32(buffer, entry + 0x08);
                uint reserved2 = ReadUInt32(buffer, entry + 0x0C);
                uint voltage = ReadUInt32(buffer, entry + 0x10);
                uint reserved3 = ReadUInt32(buffer, entry + 0x14);
                if (reserved1 != 0 || reserved2 != 0 || reserved3 != 0)
                    throw new InvalidOperationException("PerfClientLimits 条目包含非零保留字段。");

                if (mode == 0)
                {
                    if (voltage != 0)
                        throw new InvalidOperationException("PerfClientLimits mode 0 的 voltage 必须为 0。");
                }
                else if (mode == 2)
                {
                    if (id > 1 || voltage < 1 || voltage > 10000000)
                        throw new InvalidOperationException("PerfClientLimits mode 2 条目不符合 mVolt 契约。");
                    voltageLock = true;
                }
                else if (mode == 3)
                {
                    if (id != BoostLockDomain || voltage != BoostLockSentinelUv)
                        throw new InvalidOperationException("PerfClientLimits mode 3 条目不符合 Boost Lock 契约。");
                    boostLock = true;
                }
                else
                {
                    throw new InvalidOperationException("PerfClientLimits 返回未知 mode：" + mode);
                }
            }

            return new BoostLockContract { Enabled = boostLock, VoltageLockPresent = voltageLock };
        }

        internal static int ToMHz(int valueKHz)
        {
            return valueKHz / 1000;
        }

        internal static int PowerRawToPercent(uint raw)
        {
            return checked((int)(raw / 1000U));
        }

        internal static uint ToPowerRaw(int powerPercent)
        {
            if (powerPercent < 0)
                throw new ArgumentOutOfRangeException("powerPercent");
            return checked((uint)powerPercent * 1000U);
        }

        internal static int ReadInt32(byte[] buffer, int offset)
        {
            RequireRange(buffer, offset, 4);
            return BitConverter.ToInt32(buffer, offset);
        }

        internal static uint ReadUInt32(byte[] buffer, int offset)
        {
            return unchecked((uint)ReadInt32(buffer, offset));
        }

        internal static void WriteInt32(byte[] buffer, int offset, int value)
        {
            RequireRange(buffer, offset, 4);
            byte[] encoded = BitConverter.GetBytes(value);
            Buffer.BlockCopy(encoded, 0, buffer, offset, 4);
        }

        internal static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            WriteInt32(buffer, offset, unchecked((int)value));
        }

        private static int ToKHz(int valueMHz, string parameterName)
        {
            try
            {
                return checked(valueMHz * 1000);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(parameterName, "MHz 转换为 kHz 时溢出。");
            }
        }

        private static int FindEntry(byte[] buffer, int start, int stride, int count, int id)
        {
            for (int index = 0; index < count; index++)
            {
                int entry = start + index * stride;
                RequireRange(buffer, entry, stride);
                if (ReadInt32(buffer, entry) == id) return entry;
            }
            return -1;
        }

        private static int ReadBoundedCount(byte[] buffer, int offset, int maximum, string name)
        {
            int count = ReadInt32(buffer, offset);
            if (count < 0 || count > maximum)
                throw new InvalidOperationException(name + " 超出 0.." + maximum + "：" + count);
            return count;
        }

        private static void RequireVersion(byte[] buffer, uint expected, string name)
        {
            uint actual = ReadUInt32(buffer, 0);
            if (actual != expected)
                throw new InvalidOperationException(name + " 版本不匹配：0x" + actual.ToString("X8") + "，预期 0x" + expected.ToString("X8") + "。");
        }

        private static void RequireLength(byte[] buffer, int expected, string name)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (buffer.Length != expected)
                throw new ArgumentException(name + " 缓冲区大小为 0x" + buffer.Length.ToString("X") + "，预期 0x" + expected.ToString("X") + "。", "buffer");
        }

        private static void RequireRange(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || length < 0 || offset > buffer.Length - length)
                throw new ArgumentOutOfRangeException("offset", "访问超出缓冲区范围。");
        }
    }
}
