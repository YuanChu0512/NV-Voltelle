using System;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private void ReadVfCurve(GpuSnapshot result)
        {
            try
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
                VfCurveContract curve = NvApiVfLayouts.ParseCurve(info, status, control);
                for (int index = 0; index < curve.Points.Count; index++)
                    result.VfPoints.Add(curve.Points[index]);
            }
            catch (Exception ex)
            {
                result.VfError = ex.Message;
            }
        }
    }
}
