using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed class FanCoolersContract
    {
        public FanCoolersContract() { Fans = new List<FanSnapshot>(); }
        public IList<FanSnapshot> Fans { get; private set; }

        public FanSnapshot Find(uint coolerId)
        {
            for (int index = 0; index < Fans.Count; index++)
                if (Fans[index].CoolerId == coolerId) return Fans[index];
            return null;
        }
    }

    internal static class NvApiFanLayouts
    {
        private const int MaximumEntries = 32;
        private const int InfoEntriesOffset = 0x2C;
        private const int InfoEntryStride = 0x30;
        private const int StatusEntriesOffset = 0x28;
        private const int StatusEntryStride = 0x34;
        private const int ControlEntriesOffset = 0x2C;
        private const int ControlEntryStride = 0x2C;

        internal static byte[] CreateInfoRequest()
        {
            return CreateRequest(PrivateNvApiContracts.FanCoolersInfoSizeV1, PrivateNvApiContracts.FanCoolersInfoVersionV1);
        }

        internal static byte[] CreateStatusRequest()
        {
            return CreateRequest(PrivateNvApiContracts.FanCoolersStatusSizeV1, PrivateNvApiContracts.FanCoolersStatusVersionV1);
        }

        internal static byte[] CreateControlRequest()
        {
            return CreateRequest(PrivateNvApiContracts.FanCoolersControlSizeV1, PrivateNvApiContracts.FanCoolersControlVersionV1);
        }

        internal static FanCoolersContract Parse(byte[] info, byte[] status, byte[] control)
        {
            Require(info, PrivateNvApiContracts.FanCoolersInfoSizeV1, PrivateNvApiContracts.FanCoolersInfoVersionV1, "Fan Info");
            Require(status, PrivateNvApiContracts.FanCoolersStatusSizeV1, PrivateNvApiContracts.FanCoolersStatusVersionV1, "Fan Status");
            Require(control, PrivateNvApiContracts.FanCoolersControlSizeV1, PrivateNvApiContracts.FanCoolersControlVersionV1, "Fan Control");
            int infoCount = Count(info, 0x08, "Fan Info");
            int statusCount = Count(status, 0x04, "Fan Status");
            int controlCount = Count(control, 0x08, "Fan Control");
            if (infoCount == 0 || statusCount == 0 || controlCount == 0)
                throw new InvalidOperationException("驱动未返回可用风扇通道。");

            FanCoolersContract result = new FanCoolersContract();
            for (int index = 0; index < statusCount; index++)
            {
                int entry = StatusEntriesOffset + index * StatusEntryStride;
                uint coolerId = NvApiTuningLayouts.ReadUInt32(status, entry);
                int infoEntry = FindEntry(info, infoCount, InfoEntriesOffset, InfoEntryStride, coolerId);
                int controlEntry = FindEntry(control, controlCount, ControlEntriesOffset, ControlEntryStride, coolerId);
                if (infoEntry < 0 || controlEntry < 0) continue;
                uint minimum = NvApiTuningLayouts.ReadUInt32(status, entry + 0x08);
                uint maximum = NvApiTuningLayouts.ReadUInt32(status, entry + 0x0C);
                uint current = NvApiTuningLayouts.ReadUInt32(status, entry + 0x10);
                if (minimum > maximum || maximum > 1000U || current > 1000U)
                    throw new InvalidOperationException("风扇 " + coolerId + " 的 duty 范围无效。");
                result.Fans.Add(new FanSnapshot
                {
                    CoolerId = coolerId,
                    MaximumRpm = NvApiTuningLayouts.ReadUInt32(info, infoEntry + 0x0C),
                    CurrentRpm = NvApiTuningLayouts.ReadUInt32(status, entry + 0x04),
                    MinimumDutyPercent = minimum,
                    MaximumDutyPercent = maximum,
                    CurrentDutyPercent = current,
                    ControlMode = NvApiTuningLayouts.ReadUInt32(control, controlEntry + 0x08)
                });
            }
            if (result.Fans.Count == 0)
                throw new InvalidOperationException("Info、Status 与 Control 没有共同的风扇通道。");
            return result;
        }

        internal static byte[] CreateControlSet(byte[] currentControl, FanCoolersContract current, uint coolerId, bool manual, uint dutyPercent)
        {
            Require(currentControl, PrivateNvApiContracts.FanCoolersControlSizeV1, PrivateNvApiContracts.FanCoolersControlVersionV1, "Fan Control");
            if (current == null) throw new ArgumentNullException("current");
            FanSnapshot fan = current.Find(coolerId);
            if (fan == null) throw new ArgumentOutOfRangeException("coolerId", "目标风扇通道不存在。");
            if (manual && (dutyPercent < fan.MinimumDutyPercent || dutyPercent > fan.MaximumDutyPercent))
                throw new ArgumentOutOfRangeException("dutyPercent", "风扇 duty 超出驱动实时范围。");
            int count = Count(currentControl, 0x08, "Fan Control");
            int entry = FindEntry(currentControl, count, ControlEntriesOffset, ControlEntryStride, coolerId);
            if (entry < 0) throw new InvalidOperationException("Fan Control 缺少目标 cooler。");
            byte[] result = (byte[])currentControl.Clone();
            NvApiTuningLayouts.WriteUInt32(result, entry + 0x04, manual ? dutyPercent : 0U);
            NvApiTuningLayouts.WriteUInt32(result, entry + 0x08, manual ? 1U : 0U);
            return result;
        }

        private static byte[] CreateRequest(int size, uint version)
        {
            byte[] buffer = new byte[size];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, version);
            return buffer;
        }

        private static int Count(byte[] buffer, int offset, string name)
        {
            uint count = NvApiTuningLayouts.ReadUInt32(buffer, offset);
            if (count > MaximumEntries)
                throw new InvalidOperationException(name + " count 超过 32。");
            return checked((int)count);
        }

        private static int FindEntry(byte[] buffer, int count, int first, int stride, uint coolerId)
        {
            for (int index = 0; index < count; index++)
            {
                int entry = first + index * stride;
                if (entry + stride > buffer.Length) break;
                if (NvApiTuningLayouts.ReadUInt32(buffer, entry) == coolerId) return entry;
            }
            return -1;
        }

        private static void Require(byte[] buffer, int size, uint version, string name)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (buffer.Length != size) throw new ArgumentException(name + " 大小不匹配。", "buffer");
            if (NvApiTuningLayouts.ReadUInt32(buffer, 0) != version)
                throw new InvalidOperationException(name + " 版本字不匹配。");
        }
    }
}
