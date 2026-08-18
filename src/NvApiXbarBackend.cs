using System;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private void ReadXbar(GpuSnapshot result)
        {
            try
            {
                byte[] infoBuffer = GetBuffer(
                    PrivateNvApiContracts.XbarGetInfo,
                    NvApiXbarLayouts.CreateInfoRequest(),
                    "NvAPI_GPU_ClockClkDomainsGetInfo");
                XbarInfoContract info = NvApiXbarLayouts.ParseInfo(infoBuffer);

                byte[] controlBuffer = GetBuffer(
                    PrivateNvApiContracts.XbarGetControl,
                    NvApiXbarLayouts.CreateControlRequest(),
                    "NvAPI_GPU_ClockClkDomainsGetControl");
                XbarControlContract control = NvApiXbarLayouts.ParseControl(controlBuffer);

                byte[] measureBuffer = GetBuffer(
                    PrivateNvApiContracts.XbarMeasureFrequency,
                    NvApiXbarLayouts.CreateMeasureRequest(),
                    "Crossbar MeasureFrequency helper");

                result.Xbar.Flags = info.Flags;
                result.Xbar.MinimumOffsetMHz = info.MinimumOffsetMHz;
                result.Xbar.MaximumOffsetMHz = info.MaximumOffsetMHz;
                result.Xbar.CurrentOffsetKHz = control.CurrentOffsetKHz;
                result.Xbar.MeasuredFrequencyKHz = NvApiXbarLayouts.ParseMeasuredFrequency(measureBuffer);
            }
            catch (Exception ex)
            {
                result.Xbar.Error = ex.Message;
            }
        }
    }
}
