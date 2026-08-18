using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal sealed class GpuSnapshot
    {
        public GpuSnapshot()
        {
            Name = "未检测到 NVIDIA GPU";
            Vbios = "—";
            Driver = "—";
            DriverBranch = "—";
            PState = "—";
            CoreClockMHz = null;
            MemoryClockMHz = null;
            VideoClockMHz = null;
            TemperatureC = null;
            FanRpm = null;
            DedicatedMemoryMiB = null;
            AvailableMemoryMiB = null;
            Status = "尚未刷新";
            PrivateCapabilities = new Dictionary<string, bool>();
            VoltageRails = new List<VoltageRailRaw>();
            VoltageControls = new List<VoltageControlRaw>();
            Voltage = new VoltageRailsContract();
            AdcDevices = new List<AdcDeviceRaw>();
            Adc = new AdcDevicesContract();
            PowerTelemetry = new PowerTelemetryContract();
            VfPoints = new List<VfPointSnapshot>();
            Xbar = new XbarSnapshot();
            Tuning = new GpuTuningSnapshot();
            MobileRelOnlyCompatible = false;
        }

        public string Name { get; set; }
        public string Vbios { get; set; }
        public string Driver { get; set; }
        public string DriverBranch { get; set; }
        public uint? PciDeviceId { get; set; }
        public uint? PciSubsystemId { get; set; }
        public uint? PciRevisionId { get; set; }
        public uint? PciExternalDeviceId { get; set; }
        public uint? BusId { get; set; }
        public uint? BusSlotId { get; set; }
        public uint? PhysicalFrameBufferKiB { get; set; }
        public uint? ArchitectureId { get; set; }
        public uint? ArchitectureImplementationId { get; set; }
        public uint? ArchitectureRevision { get; set; }
        public string IdentityError { get; set; }
        public string PState { get; set; }
        public double? CoreClockMHz { get; set; }
        public double? MemoryClockMHz { get; set; }
        public double? VideoClockMHz { get; set; }
        public int? TemperatureC { get; set; }
        public int? FanRpm { get; set; }
        public double? DedicatedMemoryMiB { get; set; }
        public double? AvailableMemoryMiB { get; set; }
        public string Status { get; set; }
        public IDictionary<string, bool> PrivateCapabilities { get; private set; }
        public IList<VoltageRailRaw> VoltageRails { get; private set; }
        public IList<VoltageControlRaw> VoltageControls { get; private set; }
        public VoltageRailsContract Voltage { get; set; }
        public string VoltageError { get; set; }
        public IList<AdcDeviceRaw> AdcDevices { get; private set; }
        public AdcDevicesContract Adc { get; set; }
        public string AdcError { get; set; }
        public PowerTelemetryContract PowerTelemetry { get; set; }
        public string PowerTelemetryError { get; set; }
        public IList<VfPointSnapshot> VfPoints { get; private set; }
        public string VfError { get; set; }
        public XbarSnapshot Xbar { get; private set; }
        public GpuTuningSnapshot Tuning { get; private set; }
        public bool MobileRelOnlyCompatible { get; set; }
        public DateTime Timestamp { get; set; }
    }

    internal sealed class XbarSnapshot
    {
        public uint? Flags { get; set; }
        public int? CurrentOffsetKHz { get; set; }
        public int? MinimumOffsetMHz { get; set; }
        public int? MaximumOffsetMHz { get; set; }
        public uint? MeasuredFrequencyKHz { get; set; }
        public string Error { get; set; }
    }

    internal sealed class VfPointSnapshot
    {
        public int Index { get; set; }
        public uint VoltageUv { get; set; }
        public uint BaseFrequencyKHz { get; set; }
        public uint ActualFrequencyKHz { get; set; }
        public int FrequencyOffsetKHz { get; set; }
    }

    internal sealed class GpuTuningSnapshot
    {
        public GpuTuningSnapshot()
        {
            Errors = new List<string>();
        }

        public int? CoreOffsetMHz { get; set; }
        public int? CoreOffsetKHz { get; set; }
        public int? CoreMinimumMHz { get; set; }
        public int? CoreMaximumMHz { get; set; }
        public int? MemoryOffsetMHz { get; set; }
        public int? MemoryOffsetKHz { get; set; }
        public int? MemoryMinimumMHz { get; set; }
        public int? MemoryMaximumMHz { get; set; }
        public int? PowerPercent { get; set; }
        public uint? PowerRaw { get; set; }
        public int? PowerMinimumPercent { get; set; }
        public int? PowerDefaultPercent { get; set; }
        public int? PowerMaximumPercent { get; set; }
        public bool? BoostLockEnabled { get; set; }
        public IList<string> Errors { get; private set; }
    }

    internal sealed class VoltageRailRaw
    {
        public int RailIndex { get; set; }
        public uint[] Fields { get; set; }
    }

    internal sealed class VoltageControlRaw
    {
        public int RailIndex { get; set; }
        public uint[] Fields { get; set; }
    }

    internal sealed class AdcDeviceRaw
    {
        public int DeviceIndex { get; set; }
        public uint[] Fields { get; set; }
    }

    internal interface IGpuBackend : IDisposable
    {
        bool HardwareWritesEnabled { get; }
        GpuSnapshot Read();
    }
}
