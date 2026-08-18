using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private sealed class ProfileWriteSnapshot
        {
            public CompleteTuningContracts Tuning;
            public VoltageWriteSnapshot Voltage;
            public XbarWriteSnapshot Xbar;
            public VfWriteSnapshot Vf;
        }

        private sealed class ProfileWritePlan
        {
            public byte[] VoltageControl;
            public Dictionary<int, VoltageRailTargetOffsets> VoltageTargets = new Dictionary<int, VoltageRailTargetOffsets>();
            public byte[] XbarControl;
            public List<VfOffsetChange> VfChanges = new List<VfOffsetChange>();
            public List<byte[]> VfBuffers = new List<byte[]>();
        }

        internal BestEffortWriteResult ApplyProfileVerified(MVoltProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (!HardwareWritesEnabled)
                throw new InvalidOperationException("此构建的硬件写入总闸为关闭状态。");
            ProfileStore.ValidateProfile(profile);

            BestEffortWriteResult result = new BestEffortWriteResult();
            if (profile.Controls.Core.Enabled)
                result.Attempt("核心频率偏移", delegate { ApplyPstateOffsetVerified(NvApiTuningLayouts.CoreDomain, profile.Controls.Core.OffsetMHz); });
            if (profile.Controls.Memory.Enabled)
                result.Attempt("显存频率偏移", delegate { ApplyPstateOffsetVerified(NvApiTuningLayouts.MemoryDomain, profile.Controls.Memory.OffsetMHz); });
            if (profile.Controls.Power.Enabled)
                result.Attempt("功耗上限", delegate { ApplyPowerPercentVerified(profile.Controls.Power.Percent); });
            if (profile.Controls.VoltageBoost.Enabled)
                result.Attempt("Voltage Boost", delegate { ApplyVoltageBoostVerified(profile.Controls.VoltageBoost.Percent); });
            if (profile.Controls.Nvvdd.Enabled)
                result.Attempt("NVVDD", delegate
                {
                    ValidateProfileVoltageCompatibility(profile);
                    ApplyVoltageRailRangeVerified(
                        0,
                        profile.Controls.Nvvdd.MinimumMv,
                        profile.Controls.Nvvdd.MaximumMv,
                        profile.Xoc,
                        profile.MobileRelOnly);
                });
            if (profile.Controls.Msvdd.Enabled)
                result.Attempt("MSVDD", delegate
                {
                    ValidateProfileVoltageCompatibility(profile);
                    ApplyVoltageRailRangeVerified(
                        1,
                        profile.Controls.Msvdd.MinimumMv,
                        profile.Controls.Msvdd.MaximumMv,
                        profile.Xoc,
                        profile.MobileRelOnly);
                });
            if (profile.Controls.Xbar.Enabled)
                result.Attempt("Crossbar", delegate { ApplyXbarVerified(profile.Controls.Xbar.OffsetMHz); });

            if (profile.VfCurveOffsetsKHz.Count != 0)
            {
                try
                {
                    VfWriteSnapshot vf = ReadVfWriteSnapshot();
                    if (vf.Curve.Points.Count != profile.VfCurveOffsetsKHz.Count)
                        throw new InvalidOperationException("配置档 V/F 曲线与当前点布局不匹配。");
                    List<VfOffsetChange> changes = new List<VfOffsetChange>();
                    for (int index = 0; index < vf.Curve.Points.Count; index++)
                    {
                        changes.Add(new VfOffsetChange
                        {
                            Index = vf.Curve.Points[index].Index,
                            FrequencyOffsetKHz = profile.VfCurveOffsetsKHz[index]
                        });
                    }
                    result.Merge(ApplyVfOffsetsVerified(changes), "V/F");
                }
                catch (Exception ex)
                {
                    result.Attempt("V/F 曲线", delegate { throw ex; });
                }
            }
            return result;
        }

        private void ValidateProfileVoltageCompatibility(MVoltProfile profile)
        {
            if (profile.MobileRelOnly && !NvApiVoltageLayouts.IsMobileRelOnlyGpu(name))
                throw new NotSupportedException("此配置档要求兼容的 Blackwell REL-only 电压控制。当前 GPU 不匹配兼容规则。");
        }

        private ProfileWriteSnapshot CaptureProfileWriteSnapshot(MVoltProfile profile)
        {
            ProfileWriteSnapshot snapshot = new ProfileWriteSnapshot();
            if (UsesPerformanceControls(profile)) snapshot.Tuning = ReadCompleteTuningContracts();
            if (UsesVoltageControls(profile)) snapshot.Voltage = ReadVoltageWriteSnapshot();
            if (profile.Controls.Xbar.Enabled) snapshot.Xbar = ReadXbarWriteSnapshot();
            if (profile.VfCurveOffsetsKHz.Count != 0) snapshot.Vf = ReadVfWriteSnapshot();
            return snapshot;
        }

        private static bool UsesPerformanceControls(MVoltProfile profile)
        {
            return profile.Controls.Core.Enabled ||
                profile.Controls.Memory.Enabled ||
                profile.Controls.Power.Enabled;
        }

        private static bool UsesVoltageControls(MVoltProfile profile)
        {
            return profile.Controls.Nvvdd.Enabled ||
                profile.Controls.Msvdd.Enabled ||
                profile.Controls.VoltageBoost.Enabled;
        }

        private ProfileWritePlan PrepareProfileWrite(MVoltProfile profile, ProfileWriteSnapshot before)
        {
            ProfileWritePlan plan = new ProfileWritePlan();
            if (before.Tuning != null)
            {
                if (profile.Controls.Core.Enabled)
                    ValidateClock(before.Tuning.Core, profile.Controls.Core.OffsetMHz, true, "核心");
                if (profile.Controls.Memory.Enabled)
                    ValidateClock(before.Tuning.Memory, profile.Controls.Memory.OffsetMHz, false, "显存");
                if (profile.Controls.Power.Enabled)
                {
                    uint requestedPower = NvApiTuningLayouts.ToPowerRaw(profile.Controls.Power.Percent);
                    if (before.Tuning.Power.MinimumRaw > before.Tuning.Power.MaximumRaw ||
                        requestedPower < before.Tuning.Power.MinimumRaw ||
                        requestedPower > before.Tuning.Power.MaximumRaw)
                    {
                        throw new ArgumentOutOfRangeException(
                            "profile",
                            "配置档功耗上限超出驱动范围 " +
                            NvApiTuningLayouts.PowerRawToPercent(before.Tuning.Power.MinimumRaw) + ".." +
                            NvApiTuningLayouts.PowerRawToPercent(before.Tuning.Power.MaximumRaw) + "%");
                    }
                }
            }

            if (before.Voltage != null)
            {
                if (!profile.Xoc)
                {
                    for (int index = 0; index < before.Voltage.Contract.Rails.Count; index++)
                    {
                        VoltageRailContract rail = before.Voltage.Contract.Rails[index];
                        if (rail.MaximumLimitUv <= 1150000U) continue;
                        bool explicitlyEnabled = rail.RailIndex == 0
                            ? profile.Controls.Nvvdd.Enabled
                            : rail.RailIndex == 1 && profile.Controls.Msvdd.Enabled;
                        if (!explicitlyEnabled)
                            throw new InvalidOperationException("标准范围配置档必须显式包含当前高于 1.15 V 的每条电压轨。");
                    }
                }

                byte[] voltageControl = (byte[])before.Voltage.Control.Clone();
                if (profile.Controls.VoltageBoost.Enabled)
                    voltageControl = NvApiVoltageLayouts.CreateBoostSet(voltageControl, profile.Controls.VoltageBoost.Percent);
                PrepareProfileRail(
                    profile.Controls.Nvvdd,
                    0,
                    profile.Xoc,
                    profile.MobileRelOnly,
                    before.Voltage,
                    plan.VoltageTargets,
                    ref voltageControl);
                PrepareProfileRail(
                    profile.Controls.Msvdd,
                    1,
                    profile.Xoc,
                    profile.MobileRelOnly,
                    before.Voltage,
                    plan.VoltageTargets,
                    ref voltageControl);
                plan.VoltageControl = voltageControl;
            }

            if (before.Xbar != null)
            {
                plan.XbarControl = NvApiXbarLayouts.CreateControlSet(
                    before.Xbar.ControlBuffer,
                    before.Xbar.Info,
                    profile.Controls.Xbar.OffsetMHz);
            }

            if (before.Vf != null)
            {
                if (before.Vf.Curve.Points.Count != profile.VfCurveOffsetsKHz.Count)
                    throw new InvalidOperationException("配置档 V/F 曲线与当前 127 点布局不匹配。");
                for (int index = 0; index < before.Vf.Curve.Points.Count; index++)
                {
                    VfOffsetChange change = new VfOffsetChange
                    {
                        Index = before.Vf.Curve.Points[index].Index,
                        FrequencyOffsetKHz = profile.VfCurveOffsetsKHz[index]
                    };
                    byte[] single = NvApiVfLayouts.CreateControlSet(
                        before.Vf.Control,
                        before.Vf.Curve.Points,
                        new VfOffsetChange[] { change });
                    if (CountMaskBits(single, 0x04, 0x20) != 1)
                        throw new InvalidOperationException("配置档 V/F 单点 SET mask 不是单 bit。");
                    plan.VfChanges.Add(change);
                    plan.VfBuffers.Add(single);
                }
            }
            return plan;
        }

        private static void PrepareProfileRail(
            ProfileRangeControl control,
            int railIndex,
            bool allow1250Mv,
            bool mobileRelOnly,
            VoltageWriteSnapshot before,
            IDictionary<int, VoltageRailTargetOffsets> targets,
            ref byte[] voltageControl)
        {
            if (!control.Enabled) return;
            VoltageRailContract rail = before.Contract.FindRail(railIndex);
            if (rail == null)
                throw new NotSupportedException("配置档要求不可用的 rail " + railIndex + " 控制。");
            VoltageRailTargetOffsets target = NvApiVoltageLayouts.CalculateTargetOffsets(
                rail,
                control.MinimumMv,
                control.MaximumMv,
                allow1250Mv,
                mobileRelOnly);
            voltageControl = NvApiVoltageLayouts.CreateRailSet(voltageControl, rail, target);
            targets.Add(railIndex, target);
        }

        private void ApplyProfilePlan(MVoltProfile profile, ProfileWritePlan plan)
        {
            EnsureHardwareWritesEnabled();
            if (profile.Controls.Power.Enabled)
                SetPowerPercent(profile.Controls.Power.Percent);
            if (plan.VoltageControl != null)
                SetBuffer(
                    PrivateNvApiContracts.VoltRailsSetControl,
                    (byte[])plan.VoltageControl.Clone(),
                    "NvAPI_GPU_VoltVoltRailsSetControl (profile)");
            if (profile.Controls.Core.Enabled)
                SetPstateOffset(NvApiTuningLayouts.CoreDomain, profile.Controls.Core.OffsetMHz);
            if (profile.Controls.Memory.Enabled)
                SetPstateOffset(NvApiTuningLayouts.MemoryDomain, profile.Controls.Memory.OffsetMHz);
            if (plan.XbarControl != null)
                SetBuffer(
                    PrivateNvApiContracts.XbarSetControl,
                    (byte[])plan.XbarControl.Clone(),
                    "NvAPI_GPU_ClockClkDomainsSetControl (profile)");
            for (int index = 0; index < plan.VfBuffers.Count; index++)
            {
                SetBuffer(
                    PrivateNvApiContracts.VfSetControl,
                    (byte[])plan.VfBuffers[index].Clone(),
                    "NvAPI_GPU_ClockClientClkVfPointsSetControl (profile point " + plan.VfChanges[index].Index + ")");
            }
        }

        private void VerifyProfileApplied(MVoltProfile profile, ProfileWriteSnapshot before, ProfileWritePlan plan)
        {
            if (before.Tuning != null)
            {
                CompleteTuningContracts after = ReadCompleteTuningContracts();
                int expectedCoreKHz = profile.Controls.Core.Enabled
                    ? checked(profile.Controls.Core.OffsetMHz * 1000)
                    : before.Tuning.Core.CurrentOffsetKHz;
                int expectedMemoryKHz = profile.Controls.Memory.Enabled
                    ? checked(profile.Controls.Memory.OffsetMHz * 1000)
                    : before.Tuning.Memory.CurrentOffsetKHz;
                uint expectedPowerRaw = profile.Controls.Power.Enabled
                    ? NvApiTuningLayouts.ToPowerRaw(profile.Controls.Power.Percent)
                    : before.Tuning.Power.CurrentRaw;
                if (after.Core.CurrentOffsetKHz != expectedCoreKHz ||
                    after.Memory.CurrentOffsetKHz != expectedMemoryKHz ||
                    after.Power.CurrentRaw != expectedPowerRaw ||
                    after.Boost.Enabled != before.Tuning.Boost.Enabled)
                    throw new InvalidOperationException("配置档性能控制回读不一致。");
            }

            if (before.Voltage != null)
            {
                VoltageWriteSnapshot after = ReadVoltageWriteSnapshot();
                int expectedBoost = profile.Controls.VoltageBoost.Enabled
                    ? profile.Controls.VoltageBoost.Percent
                    : before.Voltage.Contract.VoltageBoostPercent;
                if (after.Contract.VoltageBoostPercent != expectedBoost)
                    throw new InvalidOperationException("配置档 Voltage Boost 回读不一致。");
                for (int index = 0; index < before.Voltage.Contract.Rails.Count; index++)
                {
                    VoltageRailContract original = before.Voltage.Contract.Rails[index];
                    VoltageRailContract current = after.Contract.FindRail(original.RailIndex);
                    if (current == null) throw new InvalidOperationException("配置档回读缺少电压轨。");
                    VoltageRailTargetOffsets target;
                    if (plan.VoltageTargets.TryGetValue(original.RailIndex, out target))
                        NvApiVoltageLayouts.ValidateRailReadBack(current, target);
                    else
                        AssertSingleVoltageRailEqual(original, current);
                }
            }

            if (before.Xbar != null)
            {
                XbarWriteSnapshot after = ReadXbarWriteSnapshot();
                int expected = checked(profile.Controls.Xbar.OffsetMHz * 1000);
                if (after.Control.CurrentOffsetKHz != expected)
                    throw new InvalidOperationException("配置档 Crossbar 回读不一致。");
            }
            if (before.Vf != null)
            {
                VfWriteSnapshot after = ReadVfWriteSnapshot();
                AssertVfOffsets(before.Vf.Curve.Points, after.Curve.Points, plan.VfChanges);
            }
        }

        private static void AssertSingleVoltageRailEqual(VoltageRailContract expected, VoltageRailContract actual)
        {
            if (expected.Type != actual.Type ||
                expected.PrimaryMaximumOffsetUv != actual.PrimaryMaximumOffsetUv ||
                expected.AlternateMaximumOffsetUv != actual.AlternateMaximumOffsetUv ||
                expected.MinimumOffsetUv != actual.MinimumOffsetUv ||
                expected.ReliabilityLimitUv != actual.ReliabilityLimitUv ||
                expected.AlternateLimitUv != actual.AlternateLimitUv ||
                expected.MaximumLimitUv != actual.MaximumLimitUv ||
                expected.MinimumLimitUv != actual.MinimumLimitUv)
                throw new InvalidOperationException("未启用的电压轨在配置档事务中发生变化。");
        }
    }
}
