using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace OneDesk.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDiagnostics.Write("Program.Main entered.");
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.ThreadException += (_, e) => AppDiagnostics.Write(e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppDiagnostics.Write(e.ExceptionObject?.ToString() ?? "Unhandled exception");

        try
        {
            ApplicationConfiguration.Initialize();
            AppDiagnostics.Write("ApplicationConfiguration initialized.");
            Application.Run(new MainForm());
            AppDiagnostics.Write("Application.Run returned.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write(ex.ToString());
            MessageBox.Show(ex.Message, "OneDesk 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
