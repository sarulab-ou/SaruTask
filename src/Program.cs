using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SaruTask
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--apply-update")
            {
                try
                {
                    int processId;
                    if (args.Length != 6 || !int.TryParse(args[1], out processId)) throw new ArgumentException("更新用の起動引数が不正です。");
                    UpdateService.ApplyAfterExit(processId, args[2], args[3], args[4], new Version(args[5]));
                }
                catch (Exception ex) { MessageBox.Show("更新を完了できませんでした。\n元のEXEまたは.previousバックアップから起動してください。\n\n" + ex.Message, "SaruTask の更新", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            bool first;
            using (var mutex = new Mutex(true, "Local\\SaruTask.Application", out first))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\SaruTask.Show"))
            {
                if (!first) { showEvent.Set(); return; }
                NativeMethods.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string warning;
                var settings = AppSettings.Load(out warning);
                using (var form = new MainForm(settings, args.Contains("--background"), showEvent, warning)) Application.Run(form);
            }
        }
    }
}
