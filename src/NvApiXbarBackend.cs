using System;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private void ReadXbar(GpuSnapshot result)
        {
            ReadClockDomain(result.Xbar, NvApiXbarLayouts.Crossbar);
            ReadClockDomain(result.SysClock, NvApiXbarLayouts.Sys);
            ReadClockDomain(result.VideoClock, NvApiXbarLayouts.Video);
        }

        private void ReadClockDomain(XbarSnapshot target, ClockDomainDescriptor domain)
        {
            try
            {
                byte[] infoBuffer = GetBuffer(
                    PrivateNvApiContracts.XbarGetInfo,
                    NvApiXbarLayouts.CreateInfoRequest(),
                    "NvAPI_GPU_ClockClkDomainsGetInfo");
                XbarInfoContract info = NvApiXbarLayouts.ParseInfo(infoBuffer, domain);

                byte[] controlBuffer = GetBuffer(
                    PrivateNvApiContracts.XbarGetControl,
                    NvApiXbarLayouts.CreateControlRequest(domain),
                    "NvAPI_GPU_ClockClkDomainsGetControl");
                XbarControlContract control = NvApiXbarLayouts.ParseControl(controlBuffer, domain);

                byte[] measureBuffer = GetBuffer(
                    PrivateNvApiContracts.XbarMeasureFrequency,
                    NvApiXbarLayouts.CreateMeasureRequest(domain),
                    domain.Name + " MeasureFrequency helper");

                target.Flags = info.Flags;
                target.MinimumOffsetMHz = info.MinimumOffsetMHz;
                target.MaximumOffsetMHz = info.MaximumOffsetMHz;
                target.CurrentOffsetKHz = control.CurrentOffsetKHz;
                target.MeasuredFrequencyKHz = NvApiXbarLayouts.ParseMeasuredFrequency(measureBuffer, domain);
            }
            catch (Exception ex)
            {
                target.Error = ex.Message;
            }
        }
    }
}
