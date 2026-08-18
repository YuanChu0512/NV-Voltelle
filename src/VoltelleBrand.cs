using System;
using System.Security.Principal;

namespace MVolt.Rebuild
{
    internal static class VoltelleBrand
    {
        internal const string ProductName = "NV Voltelle";
        internal const string ProductTagline = "NVIDIA GPU Tuning Studio";
        internal const string ProductVersion = "1.3.3";
        internal const string Maker = "Mozelle";
        internal const string BilibiliId = "Mozelle_33";
        internal const string FreeNotice = "本软件完全免费";
        internal const string RiskNotice = "超频有风险，调参需谨慎。";

        internal static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
