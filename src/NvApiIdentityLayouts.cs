using System;

namespace MVolt.Rebuild
{
    internal sealed class GpuArchitectureContract
    {
        public uint ArchitectureId { get; set; }
        public uint ImplementationId { get; set; }
        public uint Revision { get; set; }
    }

    internal static class NvApiIdentityLayouts
    {
        internal const int ArchInfoSizeV2 = 0x10;
        internal const uint ArchInfoVersionV2 = 0x00020010;

        internal static byte[] CreateArchInfoRequest()
        {
            byte[] buffer = new byte[ArchInfoSizeV2];
            NvApiTuningLayouts.WriteUInt32(buffer, 0, ArchInfoVersionV2);
            return buffer;
        }

        internal static GpuArchitectureContract ParseArchInfo(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (buffer.Length != ArchInfoSizeV2)
                throw new ArgumentException("GPU ArchInfo v2 大小必须为 0x10。", "buffer");
            uint version = NvApiTuningLayouts.ReadUInt32(buffer, 0);
            if (version != ArchInfoVersionV2)
                throw new InvalidOperationException("GPU ArchInfo v2 版本字不匹配。");
            return new GpuArchitectureContract
            {
                ArchitectureId = NvApiTuningLayouts.ReadUInt32(buffer, 0x04),
                ImplementationId = NvApiTuningLayouts.ReadUInt32(buffer, 0x08),
                Revision = NvApiTuningLayouts.ReadUInt32(buffer, 0x0C)
            };
        }
    }
}
