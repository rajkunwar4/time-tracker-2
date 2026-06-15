using System.Windows.Forms;
using TimeTrack.App.Forms;

namespace TimeTrack.App;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static void Main()
    {
        // Single instance — only one tracker per machine session.
        _mutex = new Mutex(initiallyOwned: true, "TimeTrack_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TimeTrack is already running.", "TimeTrack",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        // Login first. On success, tracking starts automatically (the main window).
        using var login = new FrmLogin();
        if (login.ShowDialog() != DialogResult.OK)
            return;

        Application.Run(new FrmMain(login.Email));

        _mutex.ReleaseMutex();
    }
}
