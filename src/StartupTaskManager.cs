using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace MVolt.Rebuild
{
    internal static class StartupTaskManager
    {
        private const string TaskName = "NV Voltelle Auto Apply";

        internal static void Configure(bool enabled, int delaySeconds)
        {
            if (!enabled)
            {
                Run("/Delete /TN \"" + TaskName + "\" /F", true);
                return;
            }
            if (delaySeconds < 10 || delaySeconds > 600)
                throw new ArgumentOutOfRangeException("delaySeconds", "开机自启延迟必须位于 10..600 秒。");
            string executable = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(executable) || executable.IndexOf('"') >= 0 || executable.IndexOf('\r') >= 0 || executable.IndexOf('\n') >= 0)
                throw new InvalidOperationException("程序路径无法安全写入计划任务。");
            int minutes = delaySeconds / 60;
            int seconds = delaySeconds % 60;
            string delay = minutes.ToString("0000", CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture);
            string taskRun = "\\\"" + executable + "\\\" --startup-auto-apply --start-in-tray --elevated";
            string arguments = "/Create /TN \"" + TaskName + "\" /TR \"" + taskRun + "\" /SC ONLOGON /DELAY " + delay + " /RL HIGHEST /F";
            Run(arguments, false);
        }

        private static void Run(string arguments, bool ignoreMissingTask)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0) return;
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                if (ignoreMissingTask && detail.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0) return;
                if (ignoreMissingTask && detail.IndexOf("找不到", StringComparison.OrdinalIgnoreCase) >= 0) return;
                throw new InvalidOperationException("计划任务操作失败（" + process.ExitCode + "）：" + detail.Trim());
            }
        }
    }
}
