using System;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private void ReadVoltageRails(GpuSnapshot result)
        {
            try
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

                VoltageRailsContract voltage = NvApiVoltageLayouts.Parse(info, control, status);
                result.Voltage = voltage;
                PopulateLegacyVoltageFields(result, voltage);
            }
            catch (Exception ex)
            {
                result.VoltageError = ex.Message;
            }
        }

        private static void PopulateLegacyVoltageFields(GpuSnapshot result, VoltageRailsContract voltage)
        {
            for (int index = 0; index < voltage.Rails.Count; index++)
            {
                VoltageRailContract rail = voltage.Rails[index];
                result.VoltageControls.Add(new VoltageControlRaw
                {
                    RailIndex = rail.RailIndex,
                    Fields = new uint[]
                    {
                        unchecked((uint)rail.Type),
                        unchecked((uint)rail.PrimaryMaximumOffsetUv),
                        unchecked((uint)rail.AlternateMaximumOffsetUv),
                        unchecked((uint)rail.ControlField3Uv),
                        unchecked((uint)rail.MinimumOffsetUv),
                        unchecked((uint)rail.ControlField5Uv),
                        unchecked((uint)rail.ControlField6Uv)
                    }
                });
                result.VoltageRails.Add(new VoltageRailRaw
                {
                    RailIndex = rail.RailIndex,
                    Fields = new uint[]
                    {
                        unchecked((uint)rail.Type),
                        rail.SensedUv,
                        rail.ReliabilityLimitUv,
                        rail.AlternateLimitUv,
                        rail.OvervoltageLimitUv,
                        rail.MaximumLimitUv,
                        rail.MinimumLimitUv,
                        unchecked((uint)rail.MarginUv),
                        rail.NoiseLimitUv,
                        rail.StatusField9,
                        rail.StatusByte10,
                        rail.StatusFlag11 ? 1U : 0U,
                        rail.StatusField12,
                        rail.StatusField13,
                        rail.StatusField14
                    }
                });
            }
        }
    }
}
