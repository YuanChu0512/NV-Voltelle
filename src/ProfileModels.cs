using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace MVolt.Rebuild
{
    [DataContract]
    internal sealed class ProfileRangeControl
    {
        [DataMember(Name = "enabled", Order = 1)]
        public bool Enabled { get; set; }

        [DataMember(Name = "min_mv", Order = 2)]
        public int MinimumMv { get; set; }

        [DataMember(Name = "max_mv", Order = 3)]
        public int MaximumMv { get; set; }
    }

    [DataContract]
    internal sealed class ProfileOffsetControl
    {
        [DataMember(Name = "enabled", Order = 1)]
        public bool Enabled { get; set; }

        [DataMember(Name = "offset_mhz", Order = 2)]
        public int OffsetMHz { get; set; }
    }

    [DataContract]
    internal sealed class ProfilePercentControl
    {
        [DataMember(Name = "enabled", Order = 1)]
        public bool Enabled { get; set; }

        [DataMember(Name = "percent", Order = 2)]
        public int Percent { get; set; }
    }

    [DataContract]
    internal sealed class ProfileControls
    {
        public ProfileControls()
        {
            Nvvdd = new ProfileRangeControl();
            Msvdd = new ProfileRangeControl();
            Core = new ProfileOffsetControl();
            Memory = new ProfileOffsetControl();
            Xbar = new ProfileOffsetControl();
            Power = new ProfilePercentControl();
            VoltageBoost = new ProfilePercentControl();
        }

        [DataMember(Name = "nvvdd", Order = 1)]
        public ProfileRangeControl Nvvdd { get; set; }

        [DataMember(Name = "msvdd", Order = 2)]
        public ProfileRangeControl Msvdd { get; set; }

        [DataMember(Name = "core", Order = 3)]
        public ProfileOffsetControl Core { get; set; }

        [DataMember(Name = "memory", Order = 4)]
        public ProfileOffsetControl Memory { get; set; }

        [DataMember(Name = "xbar", Order = 5)]
        public ProfileOffsetControl Xbar { get; set; }

        [DataMember(Name = "power", Order = 6)]
        public ProfilePercentControl Power { get; set; }

        [DataMember(Name = "voltage_boost", Order = 7)]
        public ProfilePercentControl VoltageBoost { get; set; }
    }

    [DataContract]
    internal sealed class MVoltProfile
    {
        public MVoltProfile()
        {
            Id = string.Empty;
            Name = string.Empty;
            Controls = new ProfileControls();
            VfCurveOffsetsKHz = new List<int>();
            VfCurveOffsetMode = string.Empty;
        }

        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; }

        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; }

        [DataMember(Name = "xoc", Order = 3)]
        public bool Xoc { get; set; }

        [DataMember(Name = "mobile_rel_only", Order = 4)]
        public bool MobileRelOnly { get; set; }

        [DataMember(Name = "confirmed_high_memory", Order = 5)]
        public bool ConfirmedHighMemory { get; set; }

        [DataMember(Name = "controls", Order = 6)]
        public ProfileControls Controls { get; set; }

        [DataMember(Name = "vf_curve_offsets_khz", Order = 7)]
        public List<int> VfCurveOffsetsKHz { get; set; }

        [DataMember(Name = "vf_curve_offset_mode", Order = 8)]
        public string VfCurveOffsetMode { get; set; }
    }

    [DataContract]
    internal sealed class ProfileDocument
    {
        internal const string SupportedSchema = "mvolt.profile.v1";

        public ProfileDocument()
        {
            Schema = SupportedSchema;
            VbiosId = string.Empty;
            GpuName = string.Empty;
            StartupProfileId = string.Empty;
            Profiles = new List<MVoltProfile>();
        }

        [DataMember(Name = "schema", Order = 1)]
        public string Schema { get; set; }

        [DataMember(Name = "revision", Order = 2)]
        public long Revision { get; set; }

        [DataMember(Name = "vbios_id", Order = 3)]
        public string VbiosId { get; set; }

        [DataMember(Name = "gpu_name", Order = 4)]
        public string GpuName { get; set; }

        [DataMember(Name = "startup_profile_id", Order = 5)]
        public string StartupProfileId { get; set; }

        [DataMember(Name = "minimize_to_tray_at_logon", Order = 6)]
        public bool MinimizeToTrayAtLogon { get; set; }

        [DataMember(Name = "profiles", Order = 7)]
        public List<MVoltProfile> Profiles { get; set; }
    }

    internal sealed class ProfileStore
    {
        private const long MaximumFileBytes = 1024 * 1024;
        private readonly string gpuName;
        private readonly string vbiosId;
        private readonly string legacyFilePath;
        private bool observationInitialized;
        private bool observedFileExists;
        private long observedRevision;

        internal ProfileStore(string gpuName, string vbiosId)
            : this(gpuName, vbiosId, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
        {
        }

        internal ProfileStore(string gpuName, string vbiosId, string localRoot)
        {
            if (string.IsNullOrWhiteSpace(gpuName)) throw new ArgumentException("GPU 名称为空。", "gpuName");
            if (string.IsNullOrWhiteSpace(vbiosId)) throw new ArgumentException("VBIOS ID 为空。", "vbiosId");
            this.gpuName = gpuName;
            this.vbiosId = vbiosId;
            if (string.IsNullOrEmpty(localRoot))
                throw new InvalidOperationException("无法解析 LOCALAPPDATA。");
            DirectoryPath = Path.Combine(localRoot, "NV Voltelle", "profiles");
            FilePath = Path.Combine(DirectoryPath, "profile-" + SafeFileComponent(vbiosId) + ".json");
            legacyFilePath = Path.Combine(localRoot, "mVolt.Rebuild", "profiles", "profile-" + SafeFileComponent(vbiosId) + ".json");
        }

        internal string DirectoryPath { get; private set; }
        internal string FilePath { get; private set; }

        internal ProfileDocument Load()
        {
            if (!File.Exists(FilePath) && File.Exists(legacyFilePath))
            {
                Directory.CreateDirectory(DirectoryPath);
                try { File.Copy(legacyFilePath, FilePath, false); }
                catch (IOException)
                {
                    if (!File.Exists(FilePath)) throw;
                }
            }
            if (!File.Exists(FilePath))
            {
                observationInitialized = true;
                observedFileExists = false;
                observedRevision = 0;
                return NewDocument();
            }
            ProfileDocument document = ReadDocumentFile();
            observationInitialized = true;
            observedFileExists = true;
            observedRevision = document.Revision;
            return document;
        }

        internal void Save(ProfileDocument document)
        {
            if (document == null) throw new ArgumentNullException("document");
            document.Schema = ProfileDocument.SupportedSchema;
            document.GpuName = gpuName;
            document.VbiosId = vbiosId;
            ValidateDocument(document, gpuName, vbiosId);

            if (!observationInitialized)
                throw new InvalidOperationException("保存前必须先载入配置档，以建立 revision 快照。");

            Directory.CreateDirectory(DirectoryPath);
            string mutexName = "Local\\NVVoltelleProfile_" + StableKey(vbiosId);
            using (Mutex mutex = new Mutex(false, mutexName))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("等待另一个配置档写入实例超时。");

                    bool existsNow = File.Exists(FilePath);
                    if (existsNow != observedFileExists)
                        throw new IOException(existsNow
                            ? "配置档已由另一个进程创建。请重新打开配置档页面后再保存。"
                            : "配置档已由另一个进程删除。请重新打开配置档页面后再保存。");
                    if (existsNow)
                    {
                        ProfileDocument current = ReadDocumentFile();
                        if (current.Revision != observedRevision)
                            throw new IOException("配置档已在另一个实例中改变。请重新打开配置档页面后再保存。");
                    }
                    if (document.Revision != observedRevision)
                        throw new IOException("内存中的配置档 revision 与最后载入版本不一致。");

                    long previousRevision = document.Revision;
                    document.Revision = checked(observedRevision + 1);
                    ValidateDocument(document, gpuName, vbiosId);

                    string temporary = FilePath + ".tmp." + Guid.NewGuid().ToString("N");
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
                            Serialize(stream, document);
                            stream.Flush();
                        }
                        if (new FileInfo(temporary).Length > MaximumFileBytes)
                            throw new InvalidDataException("配置档序列化结果过大。");

                        if (File.Exists(FilePath))
                            File.Replace(temporary, FilePath, FilePath + ".bak", true);
                        else
                            File.Move(temporary, FilePath);
                        observedFileExists = true;
                        observedRevision = document.Revision;
                    }
                    catch
                    {
                        document.Revision = previousRevision;
                        throw;
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
                finally
                {
                    if (acquired) mutex.ReleaseMutex();
                }
            }
        }

        private ProfileDocument ReadDocumentFile()
        {
            FileInfo info = new FileInfo(FilePath);
            if (info.Length < 2 || info.Length > MaximumFileBytes)
                throw new InvalidDataException("配置档文件大小无效。");
            ProfileDocument document;
            using (FileStream stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                document = Deserialize(stream);
            ValidateDocument(document, gpuName, vbiosId);
            return document;
        }

        internal ProfileDocument NewDocument()
        {
            return new ProfileDocument
            {
                GpuName = gpuName,
                VbiosId = vbiosId
            };
        }

        internal static byte[] SerializeToUtf8(ProfileDocument document)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Serialize(stream, document);
                return stream.ToArray();
            }
        }

        internal static ProfileDocument DeserializeFromUtf8(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            using (MemoryStream stream = new MemoryStream(data, false))
                return Deserialize(stream);
        }

        internal static void ValidateDocument(ProfileDocument document, string expectedGpuName, string expectedVbiosId)
        {
            if (document == null) throw new InvalidDataException("配置档文档为空。");
            if (!string.Equals(document.Schema, ProfileDocument.SupportedSchema, StringComparison.Ordinal))
                throw new InvalidDataException("配置档 schema 不受支持。");
            if (document.Revision < 0)
                throw new InvalidDataException("配置档 revision 无效。");
            if (!string.Equals(document.GpuName, expectedGpuName, StringComparison.Ordinal))
                throw new InvalidDataException("配置档 GPU 名称与当前显卡不匹配。");
            if (!string.Equals(document.VbiosId, expectedVbiosId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("配置档 VBIOS ID 与当前显卡不匹配。");
            if (document.Profiles == null || document.Profiles.Count > 128)
                throw new InvalidDataException("配置档 profiles 数组无效。");

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < document.Profiles.Count; index++)
            {
                MVoltProfile profile = document.Profiles[index];
                ValidateProfile(profile);
                if (!ids.Add(profile.Id)) throw new InvalidDataException("配置档 ID 重复。");
                if (!names.Add(profile.Name)) throw new InvalidDataException("配置档名称重复。");
            }
            if (!string.IsNullOrEmpty(document.StartupProfileId) && !ids.Contains(document.StartupProfileId))
                throw new InvalidDataException("启动配置档 ID 不存在。");
        }

        internal static void ValidateProfile(MVoltProfile profile)
        {
            if (profile == null) throw new InvalidDataException("配置档条目为空。");
            ValidateId(profile.Id);
            ValidateName(profile.Name);
            if (profile.Controls == null ||
                profile.Controls.Nvvdd == null || profile.Controls.Msvdd == null ||
                profile.Controls.Core == null || profile.Controls.Memory == null ||
                profile.Controls.Xbar == null || profile.Controls.Power == null ||
                profile.Controls.VoltageBoost == null)
                throw new InvalidDataException("配置档 controls 对象不完整。");

            int voltageCap = profile.Xoc ? 1250 : 1150;
            ValidateRange(profile.Controls.Nvvdd, voltageCap, "NVVDD");
            ValidateRange(profile.Controls.Msvdd, voltageCap, "MSVDD");
            ValidateOffset(profile.Controls.Core, -1000, 1000, "core");
            ValidateOffset(profile.Controls.Memory, -1000, 10000, "memory");
            ValidateOffset(profile.Controls.Xbar, -2000, 2000, "xbar");
            ValidatePercent(profile.Controls.Power, 0, 200, "power");
            ValidatePercent(profile.Controls.VoltageBoost, 0, 100, "voltage_boost");
            if (profile.Controls.Memory.Enabled && profile.Controls.Memory.OffsetMHz > 4000 && !profile.ConfirmedHighMemory)
                throw new InvalidDataException("+4000 MHz 以上的显存偏移未明确确认。");

            if (profile.VfCurveOffsetsKHz == null)
                throw new InvalidDataException("V/F offset 数组为空。");
            if (profile.VfCurveOffsetsKHz.Count == 0)
            {
                if (!string.IsNullOrEmpty(profile.VfCurveOffsetMode))
                    throw new InvalidDataException("没有 V/F offset 时 mode 必须为空。");
            }
            else
            {
                if (profile.VfCurveOffsetsKHz.Count != NvApiVfLayouts.ExpectedUsablePointCount)
                    throw new InvalidDataException("V/F 配置必须包含 127 个 offset。");
                if (!string.Equals(profile.VfCurveOffsetMode, "regional_v1", StringComparison.Ordinal))
                    throw new InvalidDataException("V/F offset mode 不受支持。");
                for (int index = 0; index < profile.VfCurveOffsetsKHz.Count; index++)
                {
                    int offset = profile.VfCurveOffsetsKHz[index];
                    if (offset < -1000000 || offset > 1000000)
                        throw new InvalidDataException("V/F offset 超出 ±1000000 kHz。");
                }
            }
        }

        private static void Serialize(Stream stream, ProfileDocument document)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProfileDocument));
            serializer.WriteObject(stream, document);
        }

        private static ProfileDocument Deserialize(Stream stream)
        {
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProfileDocument));
                return serializer.ReadObject(stream) as ProfileDocument;
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException("配置档 JSON 无法解析。", ex);
            }
        }

        private static void ValidateRange(ProfileRangeControl control, int maximumMv, string name)
        {
            if (control.MinimumMv < 250 || control.MaximumMv > maximumMv || control.MinimumMv > control.MaximumMv ||
                control.MinimumMv % 5 != 0 || control.MaximumMv % 5 != 0)
                throw new InvalidDataException(name + " 电压范围必须按 5 mV 步进且位于 250.." + maximumMv + " mV。");
        }

        private static void ValidateOffset(ProfileOffsetControl control, int minimum, int maximum, string name)
        {
            if (control.OffsetMHz < minimum || control.OffsetMHz > maximum)
                throw new InvalidDataException(name + " offset 超出 " + minimum + ".." + maximum + " MHz。");
        }

        private static void ValidatePercent(ProfilePercentControl control, int minimum, int maximum, string name)
        {
            if (control.Percent < minimum || control.Percent > maximum)
                throw new InvalidDataException(name + " 百分比超出 " + minimum + ".." + maximum + "。");
        }

        private static void ValidateId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
                throw new InvalidDataException("配置档 ID 无效。");
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    throw new InvalidDataException("配置档 ID 包含非法字符。");
            }
        }

        private static void ValidateName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 128)
                throw new InvalidDataException("配置档名称为空或过长。");
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index])) throw new InvalidDataException("配置档名称包含控制字符。");
        }

        private static string SafeFileComponent(string value)
        {
            StringBuilder result = new StringBuilder();
            for (int index = 0; index < value.Length && result.Length < 80; index++)
            {
                char c = value[index];
                result.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            }
            if (result.Length == 0) throw new InvalidOperationException("VBIOS ID 无法生成配置档文件名。");
            return result.ToString();
        }

        private static string StableKey(string value)
        {
            unchecked
            {
                uint hash = 2166136261U;
                for (int index = 0; index < value.Length; index++)
                {
                    hash ^= char.ToUpperInvariant(value[index]);
                    hash *= 16777619U;
                }
                return hash.ToString("X8");
            }
        }
    }

    internal static class ProfileFactory
    {
        internal static MVoltProfile Capture(GpuSnapshot snapshot, string name, bool confirmHighMemory, bool xocEnabled)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            MVoltProfile profile = new MVoltProfile
            {
                Id = "profile-" + Guid.NewGuid().ToString("N"),
                Name = name == null ? string.Empty : name.Trim(),
                Xoc = xocEnabled,
                MobileRelOnly = snapshot.MobileRelOnlyCompatible,
                ConfirmedHighMemory = confirmHighMemory
            };

            CaptureRail(profile.Controls.Nvvdd, snapshot.Voltage == null ? null : snapshot.Voltage.FindRail(0));
            CaptureRail(profile.Controls.Msvdd, snapshot.Voltage == null ? null : snapshot.Voltage.FindRail(1));
            CaptureOffset(profile.Controls.Core, snapshot.Tuning.CoreOffsetMHz);
            CaptureOffset(profile.Controls.Memory, snapshot.Tuning.MemoryOffsetMHz);
            CaptureOffset(profile.Controls.Xbar, snapshot.Xbar.CurrentOffsetKHz.HasValue ? (int?)(snapshot.Xbar.CurrentOffsetKHz.Value / 1000) : null);
            CapturePercent(profile.Controls.Power, snapshot.Tuning.PowerPercent);
            CapturePercent(
                profile.Controls.VoltageBoost,
                snapshot.Voltage == null ? null : (int?)snapshot.Voltage.VoltageBoostPercent);

            if (profile.Controls.Memory.Enabled && profile.Controls.Memory.OffsetMHz > 4000 && !confirmHighMemory)
                throw new InvalidOperationException("+4000 MHz 以上的显存偏移必须先明确确认。");
            if (profile.Controls.Memory.OffsetMHz <= 4000)
                profile.ConfirmedHighMemory = false;

            if (snapshot.VfPoints.Count != 0)
            {
                if (snapshot.VfPoints.Count != NvApiVfLayouts.ExpectedUsablePointCount)
                    throw new InvalidOperationException("当前 V/F 曲线不是 127 点布局。");
                for (int index = 0; index < snapshot.VfPoints.Count; index++)
                    profile.VfCurveOffsetsKHz.Add(snapshot.VfPoints[index].FrequencyOffsetKHz);
                profile.VfCurveOffsetMode = "regional_v1";
            }
            ProfileStore.ValidateProfile(profile);
            return profile;
        }

        private static void CaptureRail(ProfileRangeControl target, VoltageRailContract rail)
        {
            if (rail == null) return;
            if (rail.MinimumLimitUv % 1000U != 0 || rail.MaximumLimitUv % 1000U != 0)
                throw new InvalidOperationException("电压轨范围不是整数 mV，无法保存配置档。");
            target.Enabled = true;
            target.MinimumMv = checked((int)(rail.MinimumLimitUv / 1000U));
            target.MaximumMv = checked((int)(rail.MaximumLimitUv / 1000U));
        }

        private static void CaptureOffset(ProfileOffsetControl target, int? value)
        {
            if (!value.HasValue) return;
            target.Enabled = true;
            target.OffsetMHz = value.Value;
        }

        private static void CapturePercent(ProfilePercentControl target, int? value)
        {
            if (!value.HasValue) return;
            target.Enabled = true;
            target.Percent = value.Value;
        }
    }
}
