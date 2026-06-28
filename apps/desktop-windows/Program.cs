using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace OneDesk.Windows;

internal static class Program
{
    private const string SingleInstanceMutexName = "OneDesk_SingleInstance_3F8A2E91";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main()
    {
        AppDiagnostics.Write("Program.Main entered.");
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.ThreadException += (_, e) => AppDiagnostics.Write(e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppDiagnostics.Write(e.ExceptionObject?.ToString() ?? "Unhandled exception");

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            AppDiagnostics.Write("Another OneDesk instance is already running. Exiting.");
            MessageBox.Show("OneDesk 已经在运行中，请勿重复打开。", "OneDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

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
        finally
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // Mutex may already be abandoned; ignore.
            }
            _singleInstanceMutex.Dispose();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
