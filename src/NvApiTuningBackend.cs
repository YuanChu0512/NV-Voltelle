using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend : ITuningWriter
    {
        private sealed class CompleteTuningContracts
        {
            public PstateClockContract Core;
            public PstateClockContract Memory;
            public PowerPolicyContract Power;
            public BoostLockContract Boost;
        }

        private void ReadTuning(GpuSnapshot result)
        {
            byte[] pstates = null;
            try
            {
                pstates = GetBuffer(
                    PrivateNvApiContracts.PstatesGet,
                    NvApiTuningLayouts.CreatePstatesRequest(),
                    "NvAPI_GPU_GetPstates20");
            }
            catch (Exception ex)
            {
                result.Tuning.Errors.Add("Pstates20: " + ex.Message);
            }

            if (pstates != null)
            {
                try
                {
                    PstateClockContract core = NvApiTuningLayouts.ParsePstateClock(pstates, NvApiTuningLayouts.CoreDomain);
                    PopulateClock(result.Tuning, core, true);
                }
                catch (Exception ex)
                {
                    result.Tuning.Errors.Add("核心偏移: " + ex.Message);
                }

                try
                {
                    PstateClockContract memory = NvApiTuningLayouts.ParsePstateClock(pstates, NvApiTuningLayouts.MemoryDomain);
                    PopulateClock(result.Tuning, memory, false);
                }
                catch (Exception ex)
                {
                    result.Tuning.Errors.Add("显存偏移: " + ex.Message);
                }
            }

            try
            {
                PowerPolicyContract power = ReadPowerPolicy();
                result.Tuning.PowerPercent = NvApiTuningLayouts.PowerRawToPercent(power.CurrentRaw);
                result.Tuning.PowerRaw = power.CurrentRaw;
                result.Tuning.PowerMinimumPercent = NvApiTuningLayouts.PowerRawToPercent(power.MinimumRaw);
                result.Tuning.PowerDefaultPercent = NvApiTuningLayouts.PowerRawToPercent(power.DefaultRaw);
                result.Tuning.PowerMaximumPercent = NvApiTuningLayouts.PowerRawToPercent(power.MaximumRaw);
            }
            catch (Exception ex)
            {
                result.Tuning.Errors.Add("功耗策略: " + ex.Message);
            }

            try
            {
                result.Tuning.BoostLockEnabled = ReadBoostLock().Enabled;
            }
            catch (Exception ex)
            {
                result.Tuning.Errors.Add("Boost Lock: " + ex.Message);
            }
        }

        private static void PopulateClock(GpuTuningSnapshot target, PstateClockContract source, bool core)
        {
            int current = NvApiTuningLayouts.ToMHz(source.CurrentOffsetKHz);
            int minimum = NvApiTuningLayouts.ToMHz(source.MinimumOffsetKHz);
            int maximumKHz = source.MaximumOffsetKHz;
            if (core && maximumKHz > 1000000) maximumKHz = 1000000;
            int maximum = NvApiTuningLayouts.ToMHz(maximumKHz);

            if (core)
            {
                target.CoreOffsetMHz = current;
                target.CoreOffsetKHz = source.CurrentOffsetKHz;
                target.CoreMinimumMHz = minimum;
                target.CoreMaximumMHz = maximum;
            }
            else
            {
                target.MemoryOffsetMHz = current;
                target.MemoryOffsetKHz = source.CurrentOffsetKHz;
                target.MemoryMinimumMHz = minimum;
                target.MemoryMaximumMHz = maximum;
            }

            if (!source.GlobalEditable || !source.Editable)
                target.Errors.Add((core ? "核心" : "显存") + " P0 offset 由驱动标记为不可编辑。");
        }

        void ITuningWriter.ApplyCoreOffsetVerified(int offsetMHz)
        {
            ApplyPstateOffsetVerified(NvApiTuningLayouts.CoreDomain, offsetMHz);
        }

        void ITuningWriter.ApplyMemoryOffsetVerified(int offsetMHz)
        {
            ApplyPstateOffsetVerified(NvApiTuningLayouts.MemoryDomain, offsetMHz);
        }

        void ITuningWriter.ApplyPowerLimitVerified(int percentage)
        {
            ApplyPowerPercentVerified(percentage);
        }

        void ITuningWriter.ApplyBoostLockVerified(bool enabled)
        {
            ApplyBoostLockVerified(enabled);
        }

        internal void ApplyPstateOffsetVerified(int domainId, int requestedOffsetMHz)
        {
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                delegate { return ReadPstateClock(domainId); },
                delegate(PstateClockContract before)
                {
                    ValidateClock(before, requestedOffsetMHz, domainId == NvApiTuningLayouts.CoreDomain, domainId == NvApiTuningLayouts.CoreDomain ? "核心" : "显存");
                },
                delegate { SetPstateOffset(domainId, requestedOffsetMHz); },
                delegate
                {
                    int expectedKHz = checked(requestedOffsetMHz * 1000);
                    if (ReadPstateClock(domainId).CurrentOffsetKHz != expectedKHz)
                        throw new InvalidOperationException("Pstates20 offset 回读与请求不一致。");
                });
        }

        internal void ApplyPowerPercentVerified(int requestedPercent)
        {
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                ReadPowerPolicy,
                delegate(PowerPolicyContract before)
                {
                    uint requestedRaw = NvApiTuningLayouts.ToPowerRaw(requestedPercent);
                    if (before.MinimumRaw > before.MaximumRaw ||
                        requestedRaw < before.MinimumRaw || requestedRaw > before.MaximumRaw)
                        throw new ArgumentOutOfRangeException("requestedPercent", "功耗上限超出驱动范围。");
                },
                delegate { SetPowerPercent(requestedPercent); },
                delegate
                {
                    if (ReadPowerPolicy().CurrentRaw != NvApiTuningLayouts.ToPowerRaw(requestedPercent))
                        throw new InvalidOperationException("Power Policy 回读与请求不一致。");
                });
        }

        internal void ApplyPowerRawVerified(uint requestedRaw)
        {
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                ReadPowerPolicy,
                delegate(PowerPolicyContract before)
                {
                    if (before.MinimumRaw > before.MaximumRaw ||
                        requestedRaw < before.MinimumRaw || requestedRaw > before.MaximumRaw)
                        throw new ArgumentOutOfRangeException("requestedRaw", "功耗上限超出驱动范围。");
                },
                delegate { SetPowerRaw(requestedRaw); },
                delegate
                {
                    if (ReadPowerPolicy().CurrentRaw != requestedRaw)
                        throw new InvalidOperationException("Power Policy 回读与请求不一致。");
                });
        }

        internal void ApplyBoostLockVerified(bool enabled)
        {
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                ReadBoostLock,
                delegate { },
                delegate { SetBoostLock(enabled); },
                delegate
                {
                    if (ReadBoostLock().Enabled != enabled)
                        throw new InvalidOperationException("Boost Lock 回读与请求不一致。");
                });
        }

        private CompleteTuningContracts ReadCompleteTuningContracts()
        {
            byte[] pstates = GetBuffer(
                PrivateNvApiContracts.PstatesGet,
                NvApiTuningLayouts.CreatePstatesRequest(),
                "NvAPI_GPU_GetPstates20");
            return new CompleteTuningContracts
            {
                Core = NvApiTuningLayouts.ParsePstateClock(pstates, NvApiTuningLayouts.CoreDomain),
                Memory = NvApiTuningLayouts.ParsePstateClock(pstates, NvApiTuningLayouts.MemoryDomain),
                Power = ReadPowerPolicy(),
                Boost = ReadBoostLock()
            };
        }

        private PstateClockContract ReadPstateClock(int domainId)
        {
            byte[] pstates = GetBuffer(
                PrivateNvApiContracts.PstatesGet,
                NvApiTuningLayouts.CreatePstatesRequest(),
                "NvAPI_GPU_GetPstates20");
            return NvApiTuningLayouts.ParsePstateClock(pstates, domainId);
        }

        private PowerPolicyContract ReadPowerPolicy()
        {
            byte[] info = GetBuffer(
                PrivateNvApiContracts.PowerGetInfo,
                NvApiTuningLayouts.CreatePowerInfoRequest(),
                "NvAPI_GPU_ClientPowerPoliciesGetInfo");
            byte[] status = GetBuffer(
                PrivateNvApiContracts.PowerGetStatus,
                NvApiTuningLayouts.CreatePowerStatusRequest(),
                "NvAPI_GPU_ClientPowerPoliciesGetStatus");
            return NvApiTuningLayouts.ParsePowerPolicy(info, status, 0);
        }

        private BoostLockContract ReadBoostLock()
        {
            byte[] status = GetBuffer(
                PrivateNvApiContracts.BoostLockGetStatus,
                NvApiTuningLayouts.CreateBoostStatusRequest(),
                "NvAPI_GPU_PerfClientLimitsGetStatus");
            return NvApiTuningLayouts.ParseBoostStatus(status);
        }

        private static PstateClockContract SelectClock(CompleteTuningContracts source, int domainId)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (domainId == NvApiTuningLayouts.CoreDomain) return source.Core;
            if (domainId == NvApiTuningLayouts.MemoryDomain) return source.Memory;
            throw new ArgumentOutOfRangeException("domainId", "只支持核心域和显存域。");
        }

        private static void AssertTuningConfigurationEqual(
            CompleteTuningContracts expected,
            CompleteTuningContracts actual,
            int ignoredClockDomain,
            bool ignorePower,
            bool ignoreBoost)
        {
            if (ignoredClockDomain != NvApiTuningLayouts.CoreDomain &&
                expected.Core.CurrentOffsetKHz != actual.Core.CurrentOffsetKHz)
                throw new InvalidOperationException("核心 offset 在事务中发生意外变化。");
            if (ignoredClockDomain != NvApiTuningLayouts.MemoryDomain &&
                expected.Memory.CurrentOffsetKHz != actual.Memory.CurrentOffsetKHz)
                throw new InvalidOperationException("显存 offset 在事务中发生意外变化。");
            if (!ignorePower && expected.Power.CurrentRaw != actual.Power.CurrentRaw)
                throw new InvalidOperationException("功耗上限在事务中发生意外变化。");
            if (!ignoreBoost && expected.Boost.Enabled != actual.Boost.Enabled)
                throw new InvalidOperationException("Boost Lock 在事务中发生意外变化。");
        }

        private static void ValidateClock(PstateClockContract clock, int requestedMHz, bool core, string label)
        {
            if (!clock.GlobalEditable || !clock.Editable)
                throw new InvalidOperationException(label + " P0 offset 不可编辑。");

            byte[] setBuffer = NvApiTuningLayouts.CreatePstateSet(clock.DomainId, requestedMHz);
            int requestedKHz = NvApiTuningLayouts.ReadInt32(setBuffer, 0x28);
            int maximumKHz = clock.MaximumOffsetKHz;
            if (core && maximumKHz > 1000000) maximumKHz = 1000000;
            if (clock.MinimumOffsetKHz > maximumKHz ||
                requestedKHz < clock.MinimumOffsetKHz ||
                requestedKHz > maximumKHz)
            {
                throw new ArgumentOutOfRangeException(
                    core ? "CoreOffsetMHz" : "MemoryOffsetMHz",
                    label + "偏移超出驱动范围 " +
                    NvApiTuningLayouts.ToMHz(clock.MinimumOffsetKHz) + ".." +
                    NvApiTuningLayouts.ToMHz(maximumKHz) + " MHz");
            }
        }

        private void SetPstateOffset(int domainId, int offsetMHz)
        {
            SetPstateOffsetKHz(domainId, checked(offsetMHz * 1000));
        }

        private void SetPstateOffsetKHz(int domainId, int offsetKHz)
        {
            EnsureHardwareWritesEnabled();
            SetBuffer(
                PrivateNvApiContracts.PstatesSet,
                NvApiTuningLayouts.CreatePstateSetKHz(domainId, offsetKHz),
                "NvAPI_GPU_SetPstates20");
        }

        private void SetPowerPercent(int powerPercent)
        {
            SetPowerRaw(NvApiTuningLayouts.ToPowerRaw(powerPercent));
        }

        private void SetPowerRaw(uint powerRaw)
        {
            EnsureHardwareWritesEnabled();
            byte[] current = GetBuffer(
                PrivateNvApiContracts.PowerGetStatus,
                NvApiTuningLayouts.CreatePowerStatusRequest(),
                "NvAPI_GPU_ClientPowerPoliciesGetStatus");
            SetBuffer(
                PrivateNvApiContracts.PowerSetStatus,
                NvApiTuningLayouts.CreatePowerSetRaw(current, 0, powerRaw),
                "NvAPI_GPU_ClientPowerPoliciesSetStatus");
        }

        private void SetBoostLock(bool enabled)
        {
            EnsureHardwareWritesEnabled();
            SetBuffer(
                PrivateNvApiContracts.BoostLockSetStatus,
                NvApiTuningLayouts.CreateBoostSet(enabled),
                "NvAPI_GPU_PerfClientLimitsSetStatus");
        }

        private byte[] GetBuffer(uint id, byte[] buffer, string operation)
        {
            HandleBufferDelegate call = Resolve<HandleBufferDelegate>(id, false);
            if (call == null)
                throw new NotSupportedException(operation + " 接口不可用（0x" + id.ToString("X8") + "）。");
            InvokeBuffer(call, buffer, operation);
            return buffer;
        }

        private void SetBuffer(uint id, byte[] buffer, string operation)
        {
            EnsureHardwareWritesEnabled();
            HandleBufferDelegate call = Resolve<HandleBufferDelegate>(id, false);
            if (call == null)
                throw new NotSupportedException(operation + " 接口不可用（0x" + id.ToString("X8") + "）。");
            InvokeBuffer(call, buffer, operation);
        }

        private void InvokeBuffer(HandleBufferDelegate call, byte[] buffer, string operation)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            IntPtr native = Marshal.AllocHGlobal(buffer.Length);
            try
            {
                Marshal.Copy(buffer, 0, native, buffer.Length);
                Check(call(gpu, native), operation);
                Marshal.Copy(native, buffer, 0, buffer.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(native);
            }
        }

        private void EnsureHardwareWritesEnabled()
        {
            if (!HardwareWritesEnabled)
                throw new InvalidOperationException("此构建的硬件写入总闸为关闭状态。");
        }

    }
}
