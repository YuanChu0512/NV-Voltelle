using System;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace MVolt.Rebuild
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs eventArgs)
            {
                MessageBox.Show(eventArgs.ExceptionObject.ToString(), VoltelleBrand.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            };

            bool startInTray = false;
            bool requireElevated = false;
            bool readOnly = false;
            bool startupAutoApply = false;
#if NV_VOLTELLE_UI_QA
            // The dedicated QA binary is always interactive while every NVAPI SET
            // remains compile-time disabled. This also makes a direct double-click
            // exercise the same controls as the formal build.
            bool uiQaMode = true;
#else
            bool uiQaMode = false;
#endif
            bool uiQaTrayCycle = false;
            bool suppressUi = false;
            DiagnosticReportKind? reportKind = null;
            string reportPath = null;
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (String.Equals(argument, "--start-in-tray", StringComparison.OrdinalIgnoreCase))
                {
                    startInTray = true;
                    continue;
                }
                if (String.Equals(argument, "--read-only", StringComparison.OrdinalIgnoreCase))
                {
                    readOnly = true;
                    continue;
                }
                if (String.Equals(argument, "--startup-auto-apply", StringComparison.OrdinalIgnoreCase))
                {
                    startupAutoApply = true;
                    startInTray = true;
                    requireElevated = true;
                    continue;
                }
#if NV_VOLTELLE_UI_QA
                if (String.Equals(argument, "--ui-qa", StringComparison.OrdinalIgnoreCase))
                {
                    uiQaMode = true;
                    continue;
                }
                if (String.Equals(argument, "--ui-qa-tray-cycle", StringComparison.OrdinalIgnoreCase))
                {
                    uiQaMode = true;
                    uiQaTrayCycle = true;
                    continue;
                }
#endif
                if (String.Equals(argument, "--diagnostic", StringComparison.OrdinalIgnoreCase))
                {
                    if (reportKind.HasValue)
                    {
                        Fail("一次只能生成一种报告。", suppressUi);
                        return;
                    }
                    reportKind = DiagnosticReportKind.Diagnostic;
                    continue;
                }
                if (String.Equals(argument, "--compat-report", StringComparison.OrdinalIgnoreCase))
                {
                    if (reportKind.HasValue)
                    {
                        Fail("一次只能生成一种报告。", suppressUi);
                        return;
                    }
                    reportKind = DiagnosticReportKind.Compatibility;
                    continue;
                }
                if (String.Equals(argument, "--output", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length || String.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        Fail("--output 后必须提供报告路径。", suppressUi);
                        return;
                    }
                    reportPath = args[++index];
                    continue;
                }
                if (String.Equals(argument, "--no-ui", StringComparison.OrdinalIgnoreCase))
                {
                    suppressUi = true;
                    continue;
                }
                if (String.Equals(argument, "--elevated", StringComparison.OrdinalIgnoreCase))
                {
                    requireElevated = true;
                    continue;
                }
                Fail("不支持的命令行操作：" + argument, suppressUi);
                return;
            }
            if (reportPath != null && !reportKind.HasValue)
            {
                Fail("--output 只能与 --diagnostic 或 --compat-report 一起使用。", suppressUi);
                return;
            }
            if (suppressUi && !reportKind.HasValue)
            {
                Fail("--no-ui 只能用于报告命令。", false);
                return;
            }
            if (startInTray && reportKind.HasValue)
            {
                Fail("报告命令不能与 --start-in-tray 同时使用。", suppressUi);
                return;
            }
            if (requireElevated)
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    Fail("命令行包含 --elevated，但当前进程没有管理员权限。", suppressUi);
                    return;
                }
            }

            if (reportKind.HasValue)
            {
                try
                {
                    string written;
                    using (NvApiBackend backend = new NvApiBackend(false))
                    {
                        backend.Read();
                        Thread.Sleep(250);
                        GpuSnapshot snapshot = backend.Read();
                        written = DiagnosticReport.Write(snapshot, backend.HardwareWritesEnabled, reportKind.Value, reportPath);
                    }
                    if (!suppressUi)
                    {
                        string note = reportKind.Value == DiagnosticReportKind.Compatibility
                            ? "这是 NV Voltelle 生成的明文 GET-only 兼容报告。\n\n"
                            : string.Empty;
                        MessageBox.Show(
                            VoltelleLocalization.T(note) + VoltelleLocalization.T("报告已写入：\n") + written,
                            VoltelleBrand.ProductName,
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Environment.ExitCode = 1;
                    Fail("生成 GET-only 报告失败：\n" + ex.Message, suppressUi);
                }
                return;
            }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            bool allowHardwareWrites = !readOnly && VoltelleBrand.IsAdministrator();
            app.Run(new MainWindow(startInTray, allowHardwareWrites, readOnly, uiQaMode, uiQaTrayCycle, startupAutoApply));
        }

        private static void Fail(string message, bool suppressUi)
        {
            Environment.ExitCode = 1;
            if (!suppressUi)
            {
                MessageBox.Show(
                    VoltelleLocalization.T(message),
                    VoltelleBrand.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
