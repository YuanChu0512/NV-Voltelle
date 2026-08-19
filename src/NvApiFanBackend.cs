using System;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend
    {
        private sealed class FanWriteSnapshot
        {
            public byte[] Info;
            public byte[] Status;
            public byte[] Control;
            public FanCoolersContract Contract;
        }

        private void ReadFanControl(GpuSnapshot result)
        {
            try
            {
                FanWriteSnapshot fans = ReadFanWriteSnapshot();
                for (int index = 0; index < fans.Contract.Fans.Count; index++)
                    result.Fans.Add(fans.Contract.Fans[index]);
                if (result.FanRpm == null && result.Fans.Count != 0)
                    result.FanRpm = checked((int)result.Fans[0].CurrentRpm);
            }
            catch (Exception ex)
            {
                result.FanControlError = ex.Message;
            }
        }

        internal void ApplyFanVerified(uint coolerId, bool manual, uint dutyPercent)
        {
            byte[] requested = null;
            VerifiedWriteTransaction.Execute(
                HardwareWritesEnabled,
                ReadFanWriteSnapshot,
                delegate(FanWriteSnapshot before)
                {
                    requested = NvApiFanLayouts.CreateControlSet(before.Control, before.Contract, coolerId, manual, dutyPercent);
                },
                delegate
                {
                    SetBuffer(PrivateNvApiContracts.FanCoolersSetControl, (byte[])requested.Clone(),
                        "NvAPI_GPU_ClientFanCoolersSetControl (fan " + coolerId + ")");
                },
                delegate
                {
                    FanSnapshot after = ReadFanWriteSnapshot().Contract.Find(coolerId);
                    if (after == null) throw new InvalidOperationException("风扇通道在写后回读中消失。");
                    uint expectedMode = manual ? 1U : 0U;
                    if (after.ControlMode != expectedMode || (manual && after.CurrentDutyPercent != dutyPercent))
                        throw new InvalidOperationException("风扇写后回读与请求不一致。");
                });
        }

        private FanWriteSnapshot ReadFanWriteSnapshot()
        {
            byte[] info = GetBuffer(PrivateNvApiContracts.FanCoolersGetInfo, NvApiFanLayouts.CreateInfoRequest(),
                "NvAPI_GPU_ClientFanCoolersGetInfo");
            byte[] status = GetBuffer(PrivateNvApiContracts.FanCoolersGetStatus, NvApiFanLayouts.CreateStatusRequest(),
                "NvAPI_GPU_ClientFanCoolersGetStatus");
            byte[] control = GetBuffer(PrivateNvApiContracts.FanCoolersGetControl, NvApiFanLayouts.CreateControlRequest(),
                "NvAPI_GPU_ClientFanCoolersGetControl");
            return new FanWriteSnapshot
            {
                Info = info,
                Status = status,
                Control = control,
                Contract = NvApiFanLayouts.Parse(info, status, control)
            };
        }
    }
}
