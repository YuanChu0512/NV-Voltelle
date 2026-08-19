using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private sealed class ResetAllWriteSnapshot
        {
            public CompleteTuningContracts Tuning;
            public VoltageWriteSnapshot Voltage;
            public XbarWriteSnapshot Xbar;
            public VfWriteSnapshot Vf;
        }

        private sealed class ResetAllWritePlan
        {
            public byte[] VoltageControl;
            public byte[] XbarControl;
            public readonly List<VfOffsetChange> VfChanges = new List<VfOffsetChange>();
            public readonly List<byte[]> VfBuffers = new List<byte[]>();
        }

        internal BestEffortWriteResult ApplyAllDefaultsVerified()
        {
            EnsureHardwareWritesEnabled();
            BestEffortWriteResult result = new BestEffortWriteResult();
            result.Attempt("功耗上限", delegate
            {
                PowerPolicyContract power = ReadPowerPolicy();
                ApplyPowerRawVerified(power.DefaultRaw);
            });
            result.Attempt("电压轨与 Voltage Boost", ApplyVoltageDefaultsVerified);
            result.Attempt("核心频率偏移", delegate { ApplyPstateOffsetVerified(NvApiTuningLayouts.CoreDomain, 0); });
            result.Attempt("显存频率偏移", delegate { ApplyPstateOffsetVerified(NvApiTuningLayouts.MemoryDomain, 0); });
            result.Attempt("Boost Lock", delegate { ApplyBoostLockVerified(false); });
            result.Attempt("Crossbar", delegate { ApplyXbarVerified(0); });
            result.Attempt("SYS Clock", delegate { ApplySysClockVerified(0); });
            result.Attempt("Video Clock", delegate { ApplyVideoClockVerified(0); });
            try
            {
                FanWriteSnapshot fans = ReadFanWriteSnapshot();
                for (int fanIndex = 0; fanIndex < fans.Contract.Fans.Count; fanIndex++)
                {
                    uint coolerId = fans.Contract.Fans[fanIndex].CoolerId;
                    result.Attempt("Fan " + coolerId + " Auto", delegate { ApplyFanVerified(coolerId, false, 0); });
                }
            }
            catch (Exception ex)
            {
                result.Attempt("风扇自动控制", delegate { throw ex; });
            }

            try
            {
                VfWriteSnapshot vf = ReadVfWriteSnapshot();
                List<VfOffsetChange> changes = new List<VfOffsetChange>();
                for (int index = 0; index < vf.Curve.Points.Count; index++)
                {
                    changes.Add(new VfOffsetChange
                    {
                        Index = vf.Curve.Points[index].Index,
                        FrequencyOffsetKHz = 0
                    });
                }
                result.Merge(ApplyVfOffsetsVerified(changes), "V/F");
            }
            catch (Exception ex)
            {
                result.Attempt("V/F 曲线", delegate { throw ex; });
            }
            return result;
        }

        private void ApplyVoltageDefaultsVerified()
        {
            byte[] requested = null;
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                ReadVoltageWriteSnapshot,
                delegate(VoltageWriteSnapshot before)
                {
                    requested = NvApiVoltageLayouts.CreateResetSet(before.Control, before.Contract);
                },
                delegate
                {
                    SetBuffer(
                        PrivateNvApiContracts.VoltRailsSetControl,
                        (byte[])requested.Clone(),
                        "NvAPI_GPU_VoltVoltRailsSetControl (reset all)");
                },
                delegate(VoltageWriteSnapshot before)
                {
                    AssertVoltageReset(before.Contract, ReadVoltageWriteSnapshot().Contract);
                });
        }

        private ResetAllWriteSnapshot CaptureResetAllSnapshot()
        {
            // Capture every required interface before the first SET. If any GET fails,
            // VerifiedWriteTransaction stops here and no partial reset is possible.
            return new ResetAllWriteSnapshot
            {
                Tuning = ReadCompleteTuningContracts(),
                Voltage = ReadVoltageWriteSnapshot(),
                Xbar = ReadXbarWriteSnapshot(NvApiXbarLayouts.Crossbar),
                Vf = ReadVfWriteSnapshot()
            };
        }

        private ResetAllWritePlan PrepareResetAll(ResetAllWriteSnapshot before)
        {
            if (before == null || before.Tuning == null || before.Voltage == null ||
                before.Xbar == null || before.Vf == null)
                throw new InvalidOperationException("一键复位快照不完整。");

            ValidateClock(before.Tuning.Core, 0, true, "核心");
            ValidateClock(before.Tuning.Memory, 0, false, "显存");
            if (before.Tuning.Power.MinimumRaw > before.Tuning.Power.MaximumRaw ||
                before.Tuning.Power.DefaultRaw < before.Tuning.Power.MinimumRaw ||
                before.Tuning.Power.DefaultRaw > before.Tuning.Power.MaximumRaw)
                throw new InvalidOperationException("驱动返回的默认功耗上限不在允许范围内。");

            ResetAllWritePlan plan = new ResetAllWritePlan
            {
                VoltageControl = NvApiVoltageLayouts.CreateResetSet(
                    before.Voltage.Control,
                    before.Voltage.Contract),
                XbarControl = NvApiXbarLayouts.CreateControlSet(
                    before.Xbar.ControlBuffer,
                    before.Xbar.Info,
                    0)
            };

            if (before.Vf.Curve.Points.Count != NvApiVfLayouts.ExpectedUsablePointCount)
                throw new InvalidOperationException("一键复位要求完整的 127 点 V/F 曲线。");
            for (int index = 0; index < before.Vf.Curve.Points.Count; index++)
            {
                VfPointSnapshot point = before.Vf.Curve.Points[index];
                VfOffsetChange change = new VfOffsetChange
                {
                    Index = point.Index,
                    FrequencyOffsetKHz = 0
                };
                byte[] single = NvApiVfLayouts.CreateControlSet(
                    before.Vf.Control,
                    before.Vf.Curve.Points,
                    new VfOffsetChange[] { change });
                if (CountMaskBits(single, 0x04, 0x20) != 1)
                    throw new InvalidOperationException("一键复位 V/F SET mask 不是单 bit。");
                plan.VfChanges.Add(change);
                plan.VfBuffers.Add(single);
            }
            return plan;
        }

        private void ApplyResetAll(ResetAllWriteSnapshot before, ResetAllWritePlan plan)
        {
            if (plan == null) throw new InvalidOperationException("一键复位计划未生成。");
            EnsureHardwareWritesEnabled();
            SetPowerRaw(before.Tuning.Power.DefaultRaw);
            SetBuffer(
                PrivateNvApiContracts.VoltRailsSetControl,
                (byte[])plan.VoltageControl.Clone(),
                "NvAPI_GPU_VoltVoltRailsSetControl (reset all)");
            SetPstateOffsetKHz(NvApiTuningLayouts.CoreDomain, 0);
            SetPstateOffsetKHz(NvApiTuningLayouts.MemoryDomain, 0);
            SetBoostLock(false);
            SetBuffer(
                PrivateNvApiContracts.XbarSetControl,
                (byte[])plan.XbarControl.Clone(),
                "NvAPI_GPU_ClockClkDomainsSetControl (reset all)");
            for (int index = 0; index < plan.VfBuffers.Count; index++)
            {
                SetBuffer(
                    PrivateNvApiContracts.VfSetControl,
                    (byte[])plan.VfBuffers[index].Clone(),
                    "NvAPI_GPU_ClockClientClkVfPointsSetControl (reset point " +
                    plan.VfChanges[index].Index + ")");
            }
        }

        private void VerifyResetAll(ResetAllWriteSnapshot before)
        {
            CompleteTuningContracts tuning = ReadCompleteTuningContracts();
            if (tuning.Core.CurrentOffsetKHz != 0 ||
                tuning.Memory.CurrentOffsetKHz != 0 ||
                tuning.Power.CurrentRaw != before.Tuning.Power.DefaultRaw ||
                tuning.Boost.Enabled)
                throw new InvalidOperationException("一键复位性能控制回读不一致。");

            AssertVoltageReset(before.Voltage.Contract, ReadVoltageWriteSnapshot().Contract);
            if (ReadXbarWriteSnapshot(NvApiXbarLayouts.Crossbar).Control.CurrentOffsetKHz != 0)
                throw new InvalidOperationException("一键复位 Crossbar 回读不一致。");
            AssertAllVfOffsetsZero(ReadVfWriteSnapshot().Curve.Points);
        }

        private static void AssertVoltageReset(VoltageRailsContract before, VoltageRailsContract after)
        {
            if (before == null || after == null || before.Mask != after.Mask || after.VoltageBoostPercent != 0)
                throw new InvalidOperationException("一键复位 VoltRails mask 或 Voltage Boost 回读不一致。");
            for (int index = 0; index < before.Rails.Count; index++)
            {
                VoltageRailContract original = before.Rails[index];
                VoltageRailContract current = after.FindRail(original.RailIndex);
                if (current == null || current.Type != original.Type ||
                    current.PrimaryMaximumOffsetUv != 0 ||
                    current.AlternateMaximumOffsetUv != 0 ||
                    current.MinimumOffsetUv != 0 ||
                    current.ControlField3Uv != original.ControlField3Uv ||
                    current.ControlField5Uv != original.ControlField5Uv ||
                    current.ControlField6Uv != original.ControlField6Uv)
                    throw new InvalidOperationException("一键复位 VoltRails rail " + original.RailIndex + " Control 回读不一致。");

                long expectedReliability = (long)original.ReliabilityLimitUv - original.PrimaryMaximumOffsetUv;
                long expectedAlternate = original.AlternateLimitUv == 0
                    ? 0
                    : (long)original.AlternateLimitUv - original.AlternateMaximumOffsetUv;
                long expectedMinimum = (long)original.MinimumLimitUv - original.MinimumOffsetUv;
                if (expectedReliability < 0 || expectedReliability > UInt32.MaxValue ||
                    expectedAlternate < 0 || expectedAlternate > UInt32.MaxValue ||
                    expectedMinimum < 0 || expectedMinimum > UInt32.MaxValue ||
                    current.ReliabilityLimitUv != (uint)expectedReliability ||
                    current.MaximumLimitUv != (uint)expectedReliability ||
                    current.AlternateLimitUv != (uint)expectedAlternate ||
                    current.MinimumLimitUv != (uint)expectedMinimum)
                    throw new InvalidOperationException("一键复位 VoltRails rail " + original.RailIndex + " Status 回读不一致。");
            }
        }

        private static void AssertAllVfOffsetsZero(IList<VfPointSnapshot> points)
        {
            if (points == null || points.Count != NvApiVfLayouts.ExpectedUsablePointCount)
                throw new InvalidOperationException("一键复位 V/F 回读点集合不完整。");
            for (int index = 0; index < points.Count; index++)
                if (points[index] == null || points[index].FrequencyOffsetKHz != 0)
                    throw new InvalidOperationException("一键复位 V/F 点 " + index + " 回读不为 0。");
        }
    }
}
