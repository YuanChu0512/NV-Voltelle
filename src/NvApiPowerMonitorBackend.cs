using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private ulong[] powerMonitorEnergyBaseline;
        private uint powerMonitorEnergyMask;

        private void ReadPowerTelemetry(GpuSnapshot result)
        {
            PowerTelemetryContract telemetry = new PowerTelemetryContract();
            List<string> errors = new List<string>();

            try
            {
                byte[] infoBuffer = GetBuffer(
                    PrivateNvApiContracts.PowerMonitorGetInfo,
                    NvApiPowerMonitorLayouts.CreateInfoRequest(),
                    "NvAPI_GPU_PowerMonitorGetInfo");
                PowerMonitorInfoContract info = NvApiPowerMonitorLayouts.ParseInfo(infoBuffer);
                byte[] statusBuffer = GetBuffer(
                    PrivateNvApiContracts.PowerMonitorGetStatus,
                    NvApiPowerMonitorLayouts.CreateStatusRequest(info),
                    "NvAPI_GPU_PowerMonitorGetStatus");
                ulong[] currentEnergy = NvApiPowerMonitorLayouts.ReadEnergyCounters(info, statusBuffer);
                if (powerMonitorEnergyBaseline == null || powerMonitorEnergyMask != info.Mask)
                {
                    powerMonitorEnergyBaseline = currentEnergy;
                    powerMonitorEnergyMask = info.Mask;
                }
                telemetry.Monitor = NvApiPowerMonitorLayouts.ParseStatus(
                    info,
                    statusBuffer,
                    powerMonitorEnergyBaseline);
            }
            catch (Exception ex)
            {
                errors.Add("Power Monitor: " + ex.Message);
            }

            try
            {
                byte[] topologyBuffer = GetBuffer(
                    PrivateNvApiContracts.PowerTopologyGetStatus,
                    NvApiPowerMonitorLayouts.CreatePowerTopologyRequest(),
                    "NvAPI_GPU_ClientPowerTopologyGetStatus");
                telemetry.Topology = NvApiPowerMonitorLayouts.ParsePowerTopology(topologyBuffer);
            }
            catch (Exception ex)
            {
                errors.Add("Power Topology: " + ex.Message);
            }

            try
            {
                HandleUIntOutDelegate perfCall = Resolve<HandleUIntOutDelegate>(PrivateNvApiContracts.PerfDecreaseInfo, false);
                if (perfCall == null)
                    throw new InvalidOperationException("NVAPI 接口不可用: 0x" + PrivateNvApiContracts.PerfDecreaseInfo.ToString("X8"));
                uint mask;
                Check(perfCall(gpu, out mask), "NvAPI_GPU_GetPerfDecreaseInfo");
                telemetry.PerfDecreaseMask = mask;
                telemetry.InsufficientExternalPower = (mask & 0x10U) != 0;
                IList<string> reasons = NvApiPowerMonitorLayouts.DecodePerfDecreaseReasons(mask);
                for (int index = 0; index < reasons.Count; index++)
                    telemetry.PerfDecreaseReasons.Add(reasons[index]);
            }
            catch (Exception ex)
            {
                errors.Add("Perf Decrease: " + ex.Message);
            }

            result.PowerTelemetry = telemetry;
            if (errors.Count != 0) result.PowerTelemetryError = string.Join("；", errors.ToArray());
        }
    }
}
