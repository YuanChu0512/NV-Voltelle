using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MVolt.Rebuild
{
    internal sealed partial class NvApiBackend : IGpuBackend
    {
        private const int NvApiOk = 0;
        private const int MaxPhysicalGpus = 64;
#if MVOLT_HARDWARE_VALIDATION || NV_VOLTELLE_RELEASE
        private const bool HardwareWriteBuildGate = true;
#else
        private const bool HardwareWriteBuildGate = false;
#endif

        private const uint IdInitialize = 0x0150E828;
        private const uint IdUnload = 0xD22BDD7E;
        private const uint IdEnumPhysicalGpus = 0xE5AC921F;
        private const uint IdGetFullName = 0xCEEE8E9F;
        private const uint IdGetVbiosVersion = 0xA561FD7D;
        private const uint IdGetDriverAndBranch = 0x2926AAAD;
        private const uint IdGetPciIdentifiers = 0x2DDFB66E;
        private const uint IdGetBusId = 0x1BE0B8E5;
        private const uint IdGetBusSlotId = 0x2A0A350F;
        private const uint IdGetPhysicalFrameBufferSize = 0x46FBEB03;
        private const uint IdGetArchInfo = 0xD8265D24;
        private const uint IdGetCurrentPstate = 0x927DA4F6;
        private const uint IdGetAllClockFrequencies = 0xDCB616C3;
        private const uint IdGetThermalSettings = 0xE3640A56;
        private const uint IdGetTachReading = 0x5F608315;
        private const uint IdGetMemoryInfoEx = 0xC0599498;

        private readonly IntPtr library;
        private readonly QueryInterfaceDelegate queryInterface;
        private readonly UnloadDelegate unload;
        private readonly IntPtr gpu;
        private readonly string name;
        private readonly string vbios;
        private readonly string driver;
        private readonly string branch;
        private readonly bool runtimeHardwareWritesEnabled;

        public bool HardwareWritesEnabled { get { return HardwareWriteBuildGate && runtimeHardwareWritesEnabled; } }
        public bool HardwareWriteBuildCapable { get { return HardwareWriteBuildGate; } }

        public NvApiBackend() : this(true)
        {
        }

        public NvApiBackend(bool allowHardwareWrites)
        {
            runtimeHardwareWritesEnabled = allowHardwareWrites;
            library = NativeMethods.LoadLibrary("nvapi64.dll");
            if (library == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载 nvapi64.dll。请确认 NVIDIA 驱动已安装。");

            IntPtr queryAddress = NativeMethods.GetProcAddress(library, "nvapi_QueryInterface");
            if (queryAddress == IntPtr.Zero)
                throw new InvalidOperationException("nvapi64.dll 未导出 nvapi_QueryInterface。");

            queryInterface = (QueryInterfaceDelegate)Marshal.GetDelegateForFunctionPointer(queryAddress, typeof(QueryInterfaceDelegate));
            InitializeDelegate initialize = Resolve<InitializeDelegate>(IdInitialize, true);
            unload = Resolve<UnloadDelegate>(IdUnload, false);
            Check(initialize(), "NvAPI_Initialize");

            EnumPhysicalGpusDelegate enumerate = Resolve<EnumPhysicalGpusDelegate>(IdEnumPhysicalGpus, true);
            IntPtr[] handles = new IntPtr[MaxPhysicalGpus];
            int count;
            Check(enumerate(handles, out count), "NvAPI_EnumPhysicalGPUs");
            if (count < 1 || handles[0] == IntPtr.Zero)
                throw new InvalidOperationException("NVAPI 没有返回物理 GPU。");

            gpu = handles[0];
            name = ReadShortString<HandleStringDelegate>(IdGetFullName, "NvAPI_GPU_GetFullName");
            vbios = ReadShortString<HandleStringDelegate>(IdGetVbiosVersion, "NvAPI_GPU_GetVbiosVersionString");

            uint version;
            StringBuilder branchBuffer = new StringBuilder(64);
            DriverDelegate driverCall = Resolve<DriverDelegate>(IdGetDriverAndBranch, false);
            if (driverCall != null && driverCall(out version, branchBuffer) == NvApiOk)
            {
                driver = (version / 100) + "." + (version % 100).ToString("00");
                branch = branchBuffer.ToString();
            }
            else
            {
                driver = "—";
                branch = "—";
            }
        }

        public GpuSnapshot Read()
        {
            GpuSnapshot result = new GpuSnapshot();
            result.Name = name;
            result.Vbios = vbios;
            result.Driver = driver;
            result.DriverBranch = branch;
            try { ReadIdentityDetails(result); }
            catch (Exception ex) { result.IdentityError = ex.Message; }
            result.MobileRelOnlyCompatible = NvApiVoltageLayouts.IsMobileRelOnlyGpu(name);
            result.Timestamp = DateTime.Now;

            ReadPstate(result);
            ReadClocks(result);
            ReadThermal(result);
            ReadFan(result);
            ReadMemory(result);
            AddPrivateCapabilities(result.PrivateCapabilities);
            ReadVoltageRails(result);
            ReadAdcDevices(result);
            ReadPowerTelemetry(result);
            ReadTuning(result);
            ReadVfCurve(result);
            ReadXbar(result);
            result.Status = "NVAPI 实时采样成功";
            return result;
        }

        private void ReadIdentityDetails(GpuSnapshot result)
        {
            PciIdentifiersDelegate pci = Resolve<PciIdentifiersDelegate>(IdGetPciIdentifiers, false);
            if (pci != null)
            {
                uint deviceId;
                uint subsystemId;
                uint revisionId;
                uint externalDeviceId;
                if (pci(gpu, out deviceId, out subsystemId, out revisionId, out externalDeviceId) == NvApiOk)
                {
                    result.PciDeviceId = deviceId;
                    result.PciSubsystemId = subsystemId;
                    result.PciRevisionId = revisionId;
                    result.PciExternalDeviceId = externalDeviceId;
                }
            }

            result.BusId = ReadOptionalUInt(IdGetBusId);
            result.BusSlotId = ReadOptionalUInt(IdGetBusSlotId);
            result.PhysicalFrameBufferKiB = ReadOptionalUInt(IdGetPhysicalFrameBufferSize);

            HandleBufferDelegate arch = Resolve<HandleBufferDelegate>(IdGetArchInfo, false);
            if (arch != null)
            {
                byte[] request = NvApiIdentityLayouts.CreateArchInfoRequest();
                IntPtr buffer = Marshal.AllocHGlobal(request.Length);
                try
                {
                    Marshal.Copy(request, 0, buffer, request.Length);
                    if (arch(gpu, buffer) == NvApiOk)
                    {
                        Marshal.Copy(buffer, request, 0, request.Length);
                        GpuArchitectureContract parsed = NvApiIdentityLayouts.ParseArchInfo(request);
                        result.ArchitectureId = parsed.ArchitectureId;
                        result.ArchitectureImplementationId = parsed.ImplementationId;
                        result.ArchitectureRevision = parsed.Revision;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        private uint? ReadOptionalUInt(uint id)
        {
            HandleUIntOutDelegate call = Resolve<HandleUIntOutDelegate>(id, false);
            uint value;
            return call != null && call(gpu, out value) == NvApiOk ? (uint?)value : null;
        }

        private void ReadPstate(GpuSnapshot result)
        {
            HandleUIntOutDelegate call = Resolve<HandleUIntOutDelegate>(IdGetCurrentPstate, false);
            uint value;
            if (call != null && call(gpu, out value) == NvApiOk)
                result.PState = "P" + value;
        }

        private void ReadClocks(GpuSnapshot result)
        {
            HandleBufferDelegate call = Resolve<HandleBufferDelegate>(IdGetAllClockFrequencies, false);
            if (call == null) return;

            const int size = 264;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Zero(buffer, size);
                Marshal.WriteInt32(buffer, size | (3 << 16));
                Marshal.WriteInt32(buffer, 4, 0);
                if (call(gpu, buffer) != NvApiOk) return;

                result.CoreClockMHz = ReadClockDomain(buffer, 0);
                result.MemoryClockMHz = ReadClockDomain(buffer, 4);
                result.VideoClockMHz = ReadClockDomain(buffer, 8);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static double? ReadClockDomain(IntPtr buffer, int domain)
        {
            int offset = 8 + domain * 8;
            if ((Marshal.ReadInt32(buffer, offset) & 1) == 0) return null;
            uint khz = unchecked((uint)Marshal.ReadInt32(buffer, offset + 4));
            return khz / 1000.0;
        }

        private void ReadThermal(GpuSnapshot result)
        {
            ThermalDelegate call = Resolve<ThermalDelegate>(IdGetThermalSettings, false);
            if (call == null) return;

            const int size = 68;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Zero(buffer, size);
                Marshal.WriteInt32(buffer, size | (2 << 16));
                if (call(gpu, 15, buffer) != NvApiOk) return;
                int count = Math.Min(3, Marshal.ReadInt32(buffer, 4));
                for (int i = 0; i < count; i++)
                {
                    int entry = 8 + i * 20;
                    int target = Marshal.ReadInt32(buffer, entry + 16);
                    if (target == 1 || !result.TemperatureC.HasValue)
                        result.TemperatureC = Marshal.ReadInt32(buffer, entry + 12);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private void ReadFan(GpuSnapshot result)
        {
            HandleUIntOutDelegate call = Resolve<HandleUIntOutDelegate>(IdGetTachReading, false);
            uint rpm;
            if (call != null && call(gpu, out rpm) == NvApiOk)
                result.FanRpm = unchecked((int)rpm);
        }

        private void ReadMemory(GpuSnapshot result)
        {
            HandleBufferDelegate call = Resolve<HandleBufferDelegate>(IdGetMemoryInfoEx, false);
            if (call == null) return;

            const int size = 80;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Zero(buffer, size);
                Marshal.WriteInt32(buffer, size | (1 << 16));
                if (call(gpu, buffer) != NvApiOk) return;
                result.DedicatedMemoryMiB = unchecked((ulong)Marshal.ReadInt64(buffer, 8)) / 1048576.0;
                result.AvailableMemoryMiB = unchecked((ulong)Marshal.ReadInt64(buffer, 40)) / 1048576.0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private void AddPrivateCapabilities(IDictionary<string, bool> target)
        {
            target["PCI / Bus / Framebuffer / Arch"] = AreResolved(IdGetPciIdentifiers, IdGetBusId, IdGetBusSlotId, IdGetPhysicalFrameBufferSize, IdGetArchInfo);
            target["电压轨 Info / Status / Control"] = AreResolved(PrivateNvApiContracts.VoltRailsGetInfo, PrivateNvApiContracts.VoltRailsGetStatus, PrivateNvApiContracts.VoltRailsGetControl, PrivateNvApiContracts.VoltRailsSetControl);
            target["ADC 设备 Info / Status"] = AreResolved(PrivateNvApiContracts.AdcDevicesGetInfo, PrivateNvApiContracts.AdcDevicesGetStatus);
            target["Power Monitor / Topology / Perf Decrease"] = AreResolved(PrivateNvApiContracts.PowerMonitorGetInfo, PrivateNvApiContracts.PowerMonitorGetStatus, PrivateNvApiContracts.PowerTopologyGetStatus, PrivateNvApiContracts.PerfDecreaseInfo);
            target["V/F 曲线 Info / Status / Control"] = AreResolved(PrivateNvApiContracts.VfGetInfo, PrivateNvApiContracts.VfGetStatus, PrivateNvApiContracts.VfGetControl, PrivateNvApiContracts.VfSetControl);
            target["功耗限制 Info / Status / Control"] = AreResolved(PrivateNvApiContracts.PowerGetInfo, PrivateNvApiContracts.PowerGetStatus, PrivateNvApiContracts.PowerSetStatus);
            target["Boost Lock Status / Control"] = AreResolved(PrivateNvApiContracts.BoostLockGetStatus, PrivateNvApiContracts.BoostLockSetStatus);
            target["Crossbar Info / Get / Set / Measure"] = AreResolved(PrivateNvApiContracts.XbarGetInfo, PrivateNvApiContracts.XbarGetControl, PrivateNvApiContracts.XbarSetControl, PrivateNvApiContracts.XbarMeasureFrequency);
        }

        private void ReadAdcDevices(GpuSnapshot result)
        {
            try
            {
                byte[] info = GetBuffer(
                    PrivateNvApiContracts.AdcDevicesGetInfo,
                    NvApiAdcLayouts.CreateInfoRequest(),
                    "NvAPI_GPU_ClockAdcDevicesGetInfo");
                byte[] status = GetBuffer(
                    PrivateNvApiContracts.AdcDevicesGetStatus,
                    NvApiAdcLayouts.CreateStatusRequest(info),
                    "NvAPI_GPU_ClockAdcDevicesGetStatus");
                AdcDevicesContract adc = NvApiAdcLayouts.Parse(info, status);
                result.Adc = adc;
                for (int index = 0; index < adc.Devices.Count; index++)
                {
                    AdcDeviceContract device = adc.Devices[index];
                    uint[] fields = new uint[12];
                    fields[0] = device.RawValue;
                    fields[1] = device.CorrectedVoltageUv;
                    fields[2] = (uint)(device.StatusByte8 | (device.StatusByte9 << 8) | (device.StatusByteA << 16));
                    fields[11] = device.StatusByte2C;
                    result.AdcDevices.Add(new AdcDeviceRaw { DeviceIndex = device.DeviceIndex, Fields = fields });
                }
            }
            catch (Exception ex)
            {
                result.AdcError = ex.Message;
            }
        }

        private bool AreResolved(params uint[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
                if (queryInterface(ids[i]) == IntPtr.Zero) return false;
            return true;
        }

        private string ReadShortString<T>(uint id, string operation) where T : class
        {
            HandleStringDelegate call = Resolve<HandleStringDelegate>(id, true);
            StringBuilder text = new StringBuilder(64);
            Check(call(gpu, text), operation);
            return text.ToString();
        }

        private T Resolve<T>(uint id, bool required) where T : class
        {
            IntPtr address = queryInterface(id);
            if (address == IntPtr.Zero)
            {
                if (required) throw new InvalidOperationException("NVAPI 接口不可用: 0x" + id.ToString("X8"));
                return null;
            }
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        private static void Check(int status, string operation)
        {
            if (status != NvApiOk)
                throw new InvalidOperationException(operation + " 失败，NVAPI 状态: " + status);
        }

        private static void Zero(IntPtr buffer, int size)
        {
            byte[] zeros = new byte[size];
            Marshal.Copy(zeros, 0, buffer, size);
        }

        public void Dispose()
        {
            if (unload != null) unload();
            if (library != IntPtr.Zero) NativeMethods.FreeLibrary(library);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr QueryInterfaceDelegate(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UnloadDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EnumPhysicalGpusDelegate([Out] IntPtr[] handles, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int HandleStringDelegate(IntPtr handle, StringBuilder value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int DriverDelegate(out uint version, StringBuilder branch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int HandleUIntOutDelegate(IntPtr handle, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PciIdentifiersDelegate(
            IntPtr handle,
            out uint deviceId,
            out uint subsystemId,
            out uint revisionId,
            out uint externalDeviceId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int HandleBufferDelegate(IntPtr handle, IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ThermalDelegate(IntPtr handle, uint sensorIndex, IntPtr buffer);

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr LoadLibrary(string fileName);
            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            internal static extern IntPtr GetProcAddress(IntPtr module, string name);
            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool FreeLibrary(IntPtr module);
        }
    }
}
