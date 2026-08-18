using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MVolt.Rebuild
{
    internal enum DiagnosticReportKind
    {
        Diagnostic,
        Compatibility
    }

    internal static class DiagnosticReport
    {
        private const int MaximumReportBytes = 16 * 1024 * 1024;

        internal static string Build(GpuSnapshot snapshot, bool hardwareWritesEnabled, DiagnosticReportKind kind)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            StringBuilder json = new StringBuilder(32768);
            json.Append('{');
            Property(json, "schema", kind == DiagnosticReportKind.Compatibility
                ? "mvolt.rebuild.compat.v1"
                : "mvolt.rebuild.diagnostic.v1");
            json.Append(',');
            Property(json, "created_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            json.Append(',');
            json.Append("\"safety\":{");
            BoolProperty(json, "get_only", true);
            json.Append(',');
            BoolProperty(json, "hardware_writes_enabled_in_build", hardwareWritesEnabled);
            json.Append(',');
            BoolProperty(json, "setters_called_by_report", false);
            json.Append(',');
            BoolProperty(json, "address_scan_performed", false);
            json.Append(',');
            BoolProperty(json, "i2c_performed", false);
            json.Append("},");
            json.Append("\"privacy\":{");
            BoolProperty(json, "usernames_collected", false);
            json.Append(',');
            BoolProperty(json, "filesystem_paths_collected", false);
            json.Append(',');
            BoolProperty(json, "serial_number_api_called", false);
            json.Append("},");

            json.Append("\"gpu\":{");
            Property(json, "name", snapshot.Name);
            json.Append(',');
            Property(json, "vbios", snapshot.Vbios);
            json.Append(',');
            Property(json, "driver", snapshot.Driver);
            json.Append(',');
            Property(json, "driver_branch", snapshot.DriverBranch);
            json.Append(',');
            NullableNumberProperty(json, "pci_device_id", snapshot.PciDeviceId);
            json.Append(',');
            NullableNumberProperty(json, "pci_subsystem_id", snapshot.PciSubsystemId);
            json.Append(',');
            NullableNumberProperty(json, "pci_revision_id", snapshot.PciRevisionId);
            json.Append(',');
            NullableNumberProperty(json, "pci_external_device_id", snapshot.PciExternalDeviceId);
            json.Append(',');
            NullableNumberProperty(json, "bus_id", snapshot.BusId);
            json.Append(',');
            NullableNumberProperty(json, "bus_slot_id", snapshot.BusSlotId);
            json.Append(',');
            NullableNumberProperty(json, "physical_framebuffer_kib", snapshot.PhysicalFrameBufferKiB);
            json.Append(',');
            NullableNumberProperty(json, "architecture_id", snapshot.ArchitectureId);
            json.Append(',');
            NullableNumberProperty(json, "architecture_implementation_id", snapshot.ArchitectureImplementationId);
            json.Append(',');
            NullableNumberProperty(json, "architecture_revision", snapshot.ArchitectureRevision);
            json.Append(',');
            Property(json, "pstate", snapshot.PState);
            json.Append(',');
            NullableNumberProperty(json, "core_clock_mhz", snapshot.CoreClockMHz);
            json.Append(',');
            NullableNumberProperty(json, "memory_clock_mhz", snapshot.MemoryClockMHz);
            json.Append(',');
            NullableNumberProperty(json, "video_clock_mhz", snapshot.VideoClockMHz);
            json.Append(',');
            NullableNumberProperty(json, "temperature_c", snapshot.TemperatureC);
            json.Append(',');
            NullableNumberProperty(json, "fan_rpm", snapshot.FanRpm);
            json.Append(',');
            NullableNumberProperty(json, "dedicated_memory_mib", snapshot.DedicatedMemoryMiB);
            json.Append(',');
            NullableNumberProperty(json, "available_memory_mib", snapshot.AvailableMemoryMiB);
            json.Append(',');
            BoolProperty(json, "mobile_rel_only_compatible", snapshot.MobileRelOnlyCompatible);
            json.Append(',');
            Property(json, "identity_error", snapshot.IdentityError);
            json.Append("},");

            json.Append("\"interfaces\":[");
            bool first = true;
            foreach (KeyValuePair<string, bool> item in snapshot.PrivateCapabilities)
            {
                if (!first) json.Append(',');
                first = false;
                json.Append('{');
                Property(json, "name", item.Key);
                json.Append(',');
                BoolProperty(json, "resolved", item.Value);
                json.Append('}');
            }
            json.Append("],");

            json.Append("\"tuning\":{");
            NullableNumberProperty(json, "core_offset_mhz", snapshot.Tuning.CoreOffsetMHz);
            json.Append(',');
            NullableNumberProperty(json, "core_offset_khz_raw", snapshot.Tuning.CoreOffsetKHz);
            json.Append(',');
            NullableNumberProperty(json, "core_minimum_mhz", snapshot.Tuning.CoreMinimumMHz);
            json.Append(',');
            NullableNumberProperty(json, "core_maximum_mhz", snapshot.Tuning.CoreMaximumMHz);
            json.Append(',');
            NullableNumberProperty(json, "memory_offset_mhz", snapshot.Tuning.MemoryOffsetMHz);
            json.Append(',');
            NullableNumberProperty(json, "memory_offset_khz_raw", snapshot.Tuning.MemoryOffsetKHz);
            json.Append(',');
            NullableNumberProperty(json, "memory_minimum_mhz", snapshot.Tuning.MemoryMinimumMHz);
            json.Append(',');
            NullableNumberProperty(json, "memory_maximum_mhz", snapshot.Tuning.MemoryMaximumMHz);
            json.Append(',');
            NullableNumberProperty(json, "power_percent", snapshot.Tuning.PowerPercent);
            json.Append(',');
            NullableNumberProperty(json, "power_raw", snapshot.Tuning.PowerRaw);
            json.Append(',');
            NullableNumberProperty(json, "power_minimum_percent", snapshot.Tuning.PowerMinimumPercent);
            json.Append(',');
            NullableNumberProperty(json, "power_default_percent", snapshot.Tuning.PowerDefaultPercent);
            json.Append(',');
            NullableNumberProperty(json, "power_maximum_percent", snapshot.Tuning.PowerMaximumPercent);
            json.Append(',');
            NullableBoolProperty(json, "boost_lock", snapshot.Tuning.BoostLockEnabled);
            json.Append(',');
            StringArrayProperty(json, "errors", snapshot.Tuning.Errors);
            json.Append("},");

            json.Append("\"voltage\":{");
            Property(json, "error", snapshot.VoltageError);
            json.Append(',');
            if (snapshot.Voltage == null)
            {
                json.Append("\"boost_percent\":null,\"rails\":[]");
            }
            else
            {
                NumberProperty(json, "boost_percent", snapshot.Voltage.VoltageBoostPercent);
                json.Append(",\"rails\":[");
                for (int index = 0; index < snapshot.Voltage.Rails.Count; index++)
                {
                    if (index != 0) json.Append(',');
                    VoltageRailContract rail = snapshot.Voltage.Rails[index];
                    json.Append('{');
                    NumberProperty(json, "index", rail.RailIndex);
                    json.Append(',');
                    NumberProperty(json, "type", rail.Type);
                    json.Append(',');
                    NumberProperty(json, "sensed_uv", rail.SensedUv);
                    json.Append(',');
                    NumberProperty(json, "reliability_uv", rail.ReliabilityLimitUv);
                    json.Append(',');
                    NumberProperty(json, "alternate_uv", rail.AlternateLimitUv);
                    json.Append(',');
                    NumberProperty(json, "overvoltage_uv", rail.OvervoltageLimitUv);
                    json.Append(',');
                    NumberProperty(json, "maximum_uv", rail.MaximumLimitUv);
                    json.Append(',');
                    NumberProperty(json, "minimum_uv", rail.MinimumLimitUv);
                    json.Append(',');
                    NumberProperty(json, "noise_uv", rail.NoiseLimitUv);
                    json.Append(',');
                    NumberProperty(json, "margin_uv", rail.MarginUv);
                    json.Append(',');
                    json.Append("\"control_offsets_uv\":[")
                        .Append(rail.PrimaryMaximumOffsetUv).Append(',')
                        .Append(rail.AlternateMaximumOffsetUv).Append(',')
                        .Append(rail.ControlField3Uv).Append(',')
                        .Append(rail.MinimumOffsetUv).Append(',')
                        .Append(rail.ControlField5Uv).Append(',')
                        .Append(rail.ControlField6Uv).Append(']');
                    json.Append('}');
                }
                json.Append(']');
            }
            json.Append("},");

            json.Append("\"adc\":{");
            Property(json, "error", snapshot.AdcError);
            json.Append(",\"devices\":[");
            if (snapshot.Adc != null)
            {
                for (int index = 0; index < snapshot.Adc.Devices.Count; index++)
                {
                    if (index != 0) json.Append(',');
                    AdcDeviceContract device = snapshot.Adc.Devices[index];
                    json.Append('{');
                    NumberProperty(json, "index", device.DeviceIndex);
                    json.Append(',');
                    NumberProperty(json, "domain_id", device.DomainId);
                    json.Append(',');
                    Property(json, "domain", device.DomainName);
                    json.Append(',');
                    NumberProperty(json, "corrected_uv", device.CorrectedVoltageUv);
                    json.Append(',');
                    BoolProperty(json, "raw_valid", device.RawValid);
                    json.Append(',');
                    NumberProperty(json, "raw", device.RawValue);
                    json.Append(',');
                    NumberProperty(json, "fuse_offset", device.FuseOffset);
                    json.Append(',');
                    NumberProperty(json, "fuse_gain", device.FuseGain);
                    json.Append('}');
                }
            }
            json.Append("],");
            json.Append("\"reserved\":false},");

            AppendPower(json, snapshot.PowerTelemetry, snapshot.PowerTelemetryError);
            json.Append(',');
            AppendVf(json, snapshot.VfPoints, snapshot.VfError);
            json.Append(',');
            AppendXbar(json, snapshot.Xbar);
            json.Append('}');
            return json.ToString();
        }

        internal static string Write(
            GpuSnapshot snapshot,
            bool hardwareWritesEnabled,
            DiagnosticReportKind kind,
            string requestedPath)
        {
            string path;
            if (String.IsNullOrEmpty(requestedPath))
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (String.IsNullOrEmpty(local)) throw new InvalidOperationException("无法解析 LOCALAPPDATA。");
                string directory = Path.Combine(local, "NV Voltelle", "reports");
                Directory.CreateDirectory(directory);
                string prefix = kind == DiagnosticReportKind.Compatibility ? "compat-" : "diagnostic-";
                path = Path.Combine(
                    directory,
                    prefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                    Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            }
            else
            {
                path = Path.GetFullPath(requestedPath);
                string directory = Path.GetDirectoryName(path);
                if (String.IsNullOrEmpty(directory)) throw new InvalidOperationException("报告路径没有父目录。");
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(path)) throw new IOException("报告文件已存在，拒绝覆盖：" + path);

            byte[] bytes = new UTF8Encoding(false).GetBytes(Build(snapshot, hardwareWritesEnabled, kind));
            if (bytes.Length == 0 || bytes.Length > MaximumReportBytes)
                throw new InvalidDataException("报告大小超出 1..16 MiB 范围。");
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return path;
        }

        private static void AppendPower(StringBuilder json, PowerTelemetryContract power, string error)
        {
            json.Append("\"power\":{");
            Property(json, "error", error);
            json.Append(',');
            NullableNumberProperty(json, "perf_decrease_mask", power == null ? null : power.PerfDecreaseMask);
            json.Append(',');
            NullableBoolProperty(json, "insufficient_external_power", power == null ? null : power.InsufficientExternalPower);
            json.Append(',');
            StringArrayProperty(json, "perf_decrease_reasons", power == null ? null : power.PerfDecreaseReasons);
            json.Append(',');
            NullableNumberProperty(json, "monitor_board_w", power == null || power.Monitor == null ? null : (double?)power.Monitor.BoardPowerWatts);
            json.Append(',');
            NullableNumberProperty(json, "driver_chip_w", power == null || power.Topology == null ? null : (double?)power.Topology.ChipPowerWatts);
            json.Append(',');
            NullableNumberProperty(json, "driver_board_w", power == null || power.Topology == null ? null : (double?)power.Topology.BoardPowerWatts);
            json.Append(",\"channels\":[");
            if (power != null && power.Monitor != null)
            {
                for (int index = 0; index < power.Monitor.Channels.Count; index++)
                {
                    if (index != 0) json.Append(',');
                    PowerMonitorChannelSample channel = power.Monitor.Channels[index];
                    json.Append('{');
                    NumberProperty(json, "index", channel.ChannelIndex);
                    json.Append(',');
                    NumberProperty(json, "rail_id", channel.RailId);
                    json.Append(',');
                    Property(json, "rail", channel.RailName);
                    json.Append(',');
                    NumberProperty(json, "info_field0", channel.InfoField0);
                    json.Append(',');
                    NumberProperty(json, "power_w", channel.PowerWatts);
                    json.Append(',');
                    NumberProperty(json, "current_a", channel.CurrentAmps);
                    json.Append(',');
                    NumberProperty(json, "voltage_v", channel.VoltageVolts);
                    json.Append(',');
                    NumberProperty(json, "cumulative_energy_mj", channel.CumulativeEnergyMillijoules);
                    json.Append(',');
                    NumberProperty(json, "session_energy_wh", channel.SessionEnergyWh);
                    json.Append('}');
                }
            }
            json.Append("]}");
        }

        private static void AppendVf(StringBuilder json, IList<VfPointSnapshot> points, string error)
        {
            json.Append("\"vf_curve\":{");
            Property(json, "error", error);
            json.Append(",\"points\":[");
            if (points != null)
            {
                for (int index = 0; index < points.Count; index++)
                {
                    if (index != 0) json.Append(',');
                    VfPointSnapshot point = points[index];
                    json.Append('{');
                    NumberProperty(json, "index", point.Index);
                    json.Append(',');
                    NumberProperty(json, "voltage_uv", point.VoltageUv);
                    json.Append(',');
                    NumberProperty(json, "base_frequency_khz", point.BaseFrequencyKHz);
                    json.Append(',');
                    NumberProperty(json, "actual_frequency_khz", point.ActualFrequencyKHz);
                    json.Append(',');
                    NumberProperty(json, "offset_khz", point.FrequencyOffsetKHz);
                    json.Append('}');
                }
            }
            json.Append("]}");
        }

        private static void AppendXbar(StringBuilder json, XbarSnapshot xbar)
        {
            json.Append("\"xbar\":{");
            Property(json, "error", xbar == null ? "unavailable" : xbar.Error);
            json.Append(',');
            NullableNumberProperty(json, "flags", xbar == null ? null : xbar.Flags);
            json.Append(',');
            NullableNumberProperty(json, "current_offset_khz", xbar == null ? null : xbar.CurrentOffsetKHz);
            json.Append(',');
            NullableNumberProperty(json, "minimum_offset_mhz", xbar == null ? null : xbar.MinimumOffsetMHz);
            json.Append(',');
            NullableNumberProperty(json, "maximum_offset_mhz", xbar == null ? null : xbar.MaximumOffsetMHz);
            json.Append(',');
            NullableNumberProperty(json, "measured_frequency_khz", xbar == null ? null : xbar.MeasuredFrequencyKHz);
            json.Append('}');
        }

        private static void Property(StringBuilder json, string name, string value)
        {
            Quote(json, name);
            json.Append(':');
            if (value == null) json.Append("null");
            else Quote(json, value);
        }

        private static void BoolProperty(StringBuilder json, string name, bool value)
        {
            Quote(json, name);
            json.Append(value ? ":true" : ":false");
        }

        private static void NullableBoolProperty(StringBuilder json, string name, bool? value)
        {
            Quote(json, name);
            if (!value.HasValue) json.Append(":null");
            else json.Append(value.Value ? ":true" : ":false");
        }

        private static void NumberProperty(StringBuilder json, string name, object value)
        {
            Quote(json, name);
            json.Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void NullableNumberProperty(StringBuilder json, string name, object value)
        {
            Quote(json, name);
            json.Append(':');
            if (value == null) json.Append("null");
            else json.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void StringArrayProperty(StringBuilder json, string name, IEnumerable<string> values)
        {
            Quote(json, name);
            json.Append(":[");
            bool first = true;
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!first) json.Append(',');
                    first = false;
                    Quote(json, value ?? string.Empty);
                }
            }
            json.Append(']');
        }

        private static void Quote(StringBuilder json, string value)
        {
            json.Append('"');
            if (value != null)
            {
                for (int index = 0; index < value.Length; index++)
                {
                    char c = value[index];
                    switch (c)
                    {
                        case '"': json.Append("\\\""); break;
                        case '\\': json.Append("\\\\"); break;
                        case '\b': json.Append("\\b"); break;
                        case '\f': json.Append("\\f"); break;
                        case '\n': json.Append("\\n"); break;
                        case '\r': json.Append("\\r"); break;
                        case '\t': json.Append("\\t"); break;
                        default:
                            if (c < 0x20) json.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                            else json.Append(c);
                            break;
                    }
                }
            }
            json.Append('"');
        }
    }
}
