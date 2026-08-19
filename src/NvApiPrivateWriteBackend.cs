using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private sealed class VoltageWriteSnapshot
        {
            public byte[] Info;
            public byte[] Control;
            public byte[] Status;
            public VoltageRailsContract Contract;
        }

        private sealed class VfWriteSnapshot
        {
            public byte[] Info;
            public byte[] Status;
            public byte[] Control;
            public VfCurveContract Curve;
        }

        private sealed class XbarWriteSnapshot
        {
            public byte[] InfoBuffer;
            public byte[] ControlBuffer;
            public byte[] MeasureBuffer;
            public XbarInfoContract Info;
            public XbarControlContract Control;
            public uint MeasuredFrequencyKHz;
        }

        internal void ApplyVoltageBoostVerified(int percentage)
        {
            byte[] requested = null;
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                ReadVoltageWriteSnapshot,
                delegate(VoltageWriteSnapshot before)
                {
                    requested = NvApiVoltageLayouts.CreateBoostSet(before.Control, percentage);
                },
                delegate
                {
                    SetBuffer(
                        PrivateNvApiContracts.VoltRailsSetControl,
                        (byte[])requested.Clone(),
                        "NvAPI_GPU_VoltVoltRailsSetControl (Voltage Boost)");
                },
                delegate(VoltageWriteSnapshot before)
                {
                    VoltageWriteSnapshot after = ReadVoltageWriteSnapshot();
                    if (after.Contract.VoltageBoostPercent != percentage)
                        throw new InvalidOperationException("Voltage Boost 回读与请求不一致。");
                    AssertVoltageConfigurationEqual(before.Contract, after.Contract, -1, false);
                });
        }

        internal void ApplyVoltageRailRangeVerified(
            int railIndex,
            int targetMinimumMv,
            int targetMaximumMv,
            bool allow1250Mv,
            bool mobileRelOnly)
        {
            VoltageRailTargetOffsets target = null;
            byte[] requested = null;
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                ReadVoltageWriteSnapshot,
                delegate(VoltageWriteSnapshot before)
                {
                    VoltageRailContract rail = before.Contract.FindRail(railIndex);
                    if (rail == null)
                        throw new ArgumentOutOfRangeException("railIndex", "目标电压轨不可用。");
                    target = NvApiVoltageLayouts.CalculateTargetOffsets(
                        rail,
                        targetMinimumMv,
                        targetMaximumMv,
                        allow1250Mv,
                        mobileRelOnly);
                    requested = NvApiVoltageLayouts.CreateRailSet(before.Control, rail, target);
                },
                delegate
                {
                    SetBuffer(
                        PrivateNvApiContracts.VoltRailsSetControl,
                        (byte[])requested.Clone(),
                        "NvAPI_GPU_VoltVoltRailsSetControl (rail " + railIndex + ")");
                },
                delegate(VoltageWriteSnapshot before)
                {
                    VoltageWriteSnapshot after = ReadVoltageWriteSnapshot();
                    VoltageRailContract rail = after.Contract.FindRail(railIndex);
                    if (rail == null)
                        throw new InvalidOperationException("写后回读缺少目标电压轨。");
                    NvApiVoltageLayouts.ValidateRailReadBack(rail, target);
                    AssertVoltageConfigurationEqual(before.Contract, after.Contract, railIndex, true);
                });
        }

        internal void ApplyXbarVerified(int requestedOffsetMHz)
        {
            ApplyClockDomainVerified(requestedOffsetMHz, NvApiXbarLayouts.Crossbar);
        }

        internal void ApplySysClockVerified(int requestedOffsetMHz)
        {
            ApplyClockDomainVerified(requestedOffsetMHz, NvApiXbarLayouts.Sys);
        }

        internal void ApplyVideoClockVerified(int requestedOffsetMHz)
        {
            ApplyClockDomainVerified(requestedOffsetMHz, NvApiXbarLayouts.Video);
        }

        private void ApplyClockDomainVerified(int requestedOffsetMHz, ClockDomainDescriptor domain)
        {
            byte[] requested = null;
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                delegate { return ReadXbarWriteSnapshot(domain); },
                delegate(XbarWriteSnapshot before)
                {
                    requested = NvApiXbarLayouts.CreateControlSet(
                        before.ControlBuffer,
                        before.Info,
                        requestedOffsetMHz,
                        domain);
                },
                delegate
                {
                    SetBuffer(
                        PrivateNvApiContracts.XbarSetControl,
                        (byte[])requested.Clone(),
                        "NvAPI_GPU_ClockClkDomainsSetControl (" + domain.Name + ")");
                },
                delegate
                {
                    XbarWriteSnapshot after = ReadXbarWriteSnapshot(domain);
                    int expectedKHz = checked(requestedOffsetMHz * 1000);
                    if (after.Control.CurrentOffsetKHz != expectedKHz)
                        throw new InvalidOperationException(domain.Name + " offset 回读与请求不一致。");
                });
        }

        internal BestEffortWriteResult ApplyVfOffsetsVerified(IList<VfOffsetChange> changes)
        {
            if (changes == null) throw new ArgumentNullException("changes");
            EnsureHardwareWritesEnabled();
            List<VfOffsetChange> requestedChanges = CopyVfChanges(changes);
            if (requestedChanges.Count == 0)
                throw new ArgumentException("至少需要一个 V/F 目标点。", "changes");

            VfWriteSnapshot before = ReadVfWriteSnapshot();
            HashSet<int> seen = new HashSet<int>();
            List<byte[]> onePointBuffers = new List<byte[]>();
            for (int index = 0; index < requestedChanges.Count; index++)
            {
                VfOffsetChange change = requestedChanges[index];
                if (!seen.Add(change.Index))
                    throw new ArgumentException("V/F 点 " + change.Index + " 重复。", "changes");
                byte[] single = NvApiVfLayouts.CreateControlSet(
                    before.Control,
                    before.Curve.Points,
                    new VfOffsetChange[] { change });
                if (CountMaskBits(single, 0x04, 0x20) != 1)
                    throw new InvalidOperationException("V/F 单点 SET mask 不是单 bit。");
                onePointBuffers.Add(single);
            }

            BestEffortWriteResult result = new BestEffortWriteResult();
            for (int index = 0; index < onePointBuffers.Count; index++)
            {
                int currentIndex = index;
                VfOffsetChange change = requestedChanges[currentIndex];
                result.Attempt("点 " + change.Index, delegate
                {
                    SetBuffer(
                        PrivateNvApiContracts.VfSetControl,
                        (byte[])onePointBuffers[currentIndex].Clone(),
                        "NvAPI_GPU_ClockClientClkVfPointsSetControl (point " + change.Index + ")");
                    Dictionary<int, VfPointSnapshot> after = IndexVfPoints(ReadVfWriteSnapshot().Curve.Points);
                    VfPointSnapshot point;
                    if (!after.TryGetValue(change.Index, out point) ||
                        point.FrequencyOffsetKHz != change.FrequencyOffsetKHz)
                        throw new InvalidOperationException("V/F 点 " + change.Index + " 回读与请求不一致。");
                });
            }
            return result;
        }

        private VoltageWriteSnapshot ReadVoltageWriteSnapshot()
        {
            byte[] info = GetBuffer(
                PrivateNvApiContracts.VoltRailsGetInfo,
                NvApiVoltageLayouts.CreateInfoRequest(),
                "NvAPI_GPU_VoltVoltRailsGetInfo");
            byte[] control = GetBuffer(
                PrivateNvApiContracts.VoltRailsGetControl,
                NvApiVoltageLayouts.CreateControlRequest(info),
                "NvAPI_GPU_VoltVoltRailsGetControl");
            byte[] status = GetBuffer(
                PrivateNvApiContracts.VoltRailsGetStatus,
                NvApiVoltageLayouts.CreateStatusRequest(info),
                "NvAPI_GPU_VoltVoltRailsGetStatus");
            return new VoltageWriteSnapshot
            {
                Info = info,
                Control = control,
                Status = status,
                Contract = NvApiVoltageLayouts.Parse(info, control, status)
            };
        }

        private VfWriteSnapshot ReadVfWriteSnapshot()
        {
            byte[] info = GetBuffer(
                PrivateNvApiContracts.VfGetInfo,
                NvApiVfLayouts.CreateInfoRequest(),
                "NvAPI_GPU_ClockClientClkVfPointsGetInfo");
            byte[] status = GetBuffer(
                PrivateNvApiContracts.VfGetStatus,
                NvApiVfLayouts.CreateStatusRequest(info),
                "NvAPI_GPU_ClockClientClkVfPointsGetStatus");
            byte[] control = GetBuffer(
                PrivateNvApiContracts.VfGetControl,
                NvApiVfLayouts.CreateControlRequest(info),
                "NvAPI_GPU_ClockClientClkVfPointsGetControl");
            return new VfWriteSnapshot
            {
                Info = info,
                Status = status,
                Control = control,
                Curve = NvApiVfLayouts.ParseCurve(info, status, control)
            };
        }

        private XbarWriteSnapshot ReadXbarWriteSnapshot(ClockDomainDescriptor domain)
        {
            byte[] infoBuffer = GetBuffer(
                PrivateNvApiContracts.XbarGetInfo,
                NvApiXbarLayouts.CreateInfoRequest(),
                "NvAPI_GPU_ClockClkDomainsGetInfo");
            byte[] controlBuffer = GetBuffer(
                PrivateNvApiContracts.XbarGetControl,
                NvApiXbarLayouts.CreateControlRequest(domain),
                "NvAPI_GPU_ClockClkDomainsGetControl");
            byte[] measureBuffer = GetBuffer(
                PrivateNvApiContracts.XbarMeasureFrequency,
                NvApiXbarLayouts.CreateMeasureRequest(domain),
                domain.Name + " MeasureFrequency helper");
            return new XbarWriteSnapshot
            {
                InfoBuffer = infoBuffer,
                ControlBuffer = controlBuffer,
                MeasureBuffer = measureBuffer,
                Info = NvApiXbarLayouts.ParseInfo(infoBuffer, domain),
                Control = NvApiXbarLayouts.ParseControl(controlBuffer, domain),
                MeasuredFrequencyKHz = NvApiXbarLayouts.ParseMeasuredFrequency(measureBuffer, domain)
            };
        }

        private static void AssertVoltageConfigurationEqual(
            VoltageRailsContract expected,
            VoltageRailsContract actual,
            int ignoredRail,
            bool compareBoost)
        {
            if (expected == null || actual == null)
                throw new InvalidOperationException("VoltRails 配置不可用。");
            if (expected.Mask != actual.Mask)
                throw new InvalidOperationException("VoltRails mask 在事务中发生变化。");
            if (compareBoost && expected.VoltageBoostPercent != actual.VoltageBoostPercent)
                throw new InvalidOperationException("Voltage Boost 在事务中发生意外变化。");
            for (int index = 0; index < expected.Rails.Count; index++)
            {
                VoltageRailContract left = expected.Rails[index];
                if (left.RailIndex == ignoredRail) continue;
                VoltageRailContract right = actual.FindRail(left.RailIndex);
                if (right == null ||
                    left.Type != right.Type ||
                    left.PrimaryMaximumOffsetUv != right.PrimaryMaximumOffsetUv ||
                    left.AlternateMaximumOffsetUv != right.AlternateMaximumOffsetUv ||
                    left.MinimumOffsetUv != right.MinimumOffsetUv ||
                    left.ReliabilityLimitUv != right.ReliabilityLimitUv ||
                    left.AlternateLimitUv != right.AlternateLimitUv ||
                    left.MaximumLimitUv != right.MaximumLimitUv ||
                    left.MinimumLimitUv != right.MinimumLimitUv)
                {
                    throw new InvalidOperationException("VoltRails rail " + left.RailIndex + " 在事务中发生意外变化。");
                }
            }
        }

        private static List<VfOffsetChange> CopyVfChanges(IList<VfOffsetChange> source)
        {
            List<VfOffsetChange> result = new List<VfOffsetChange>();
            for (int index = 0; index < source.Count; index++)
            {
                VfOffsetChange change = source[index];
                if (change == null) throw new ArgumentException("changes 包含 null。", "changes");
                result.Add(new VfOffsetChange
                {
                    Index = change.Index,
                    FrequencyOffsetKHz = change.FrequencyOffsetKHz
                });
            }
            return result;
        }

        private static void AssertVfOffsets(
            IList<VfPointSnapshot> before,
            IList<VfPointSnapshot> after,
            IList<VfOffsetChange> changes)
        {
            Dictionary<int, int> expectedChanges = new Dictionary<int, int>();
            for (int index = 0; index < changes.Count; index++)
                expectedChanges[changes[index].Index] = changes[index].FrequencyOffsetKHz;
            Dictionary<int, VfPointSnapshot> afterByIndex = IndexVfPoints(after);
            for (int index = 0; index < before.Count; index++)
            {
                VfPointSnapshot original = before[index];
                VfPointSnapshot current;
                if (!afterByIndex.TryGetValue(original.Index, out current))
                    throw new InvalidOperationException("V/F 回读缺少点 " + original.Index + "。");
                int expected;
                if (!expectedChanges.TryGetValue(original.Index, out expected))
                    expected = original.FrequencyOffsetKHz;
                if (current.FrequencyOffsetKHz != expected)
                    throw new InvalidOperationException("V/F 点 " + original.Index + " 回读与请求不一致。");
            }
        }

        private static Dictionary<int, VfPointSnapshot> IndexVfPoints(IList<VfPointSnapshot> points)
        {
            Dictionary<int, VfPointSnapshot> result = new Dictionary<int, VfPointSnapshot>();
            if (points == null) throw new ArgumentNullException("points");
            for (int index = 0; index < points.Count; index++)
            {
                VfPointSnapshot point = points[index];
                if (point == null || result.ContainsKey(point.Index))
                    throw new InvalidOperationException("V/F 回读点集合无效。");
                result.Add(point.Index, point);
            }
            return result;
        }

        private static int CountMaskBits(byte[] buffer, int offset, int length)
        {
            int count = 0;
            for (int index = 0; index < length; index++)
            {
                byte value = buffer[offset + index];
                for (int bit = 0; bit < 8; bit++)
                    if ((value & (1 << bit)) != 0) count++;
            }
            return count;
        }
    }
}
