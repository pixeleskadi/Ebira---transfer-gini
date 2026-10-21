namespace Ebira.Windows;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { MessageBox.Show("This Ebira build requires Windows 11 or newer."); return; }
        using var mutex = new Mutex(true, "Local\\Ebira.Native.V1", out var created);
        if (!created) { MessageBox.Show("Ebira is already running. Check its window or taskbar."); return; }
        Application.Run(new MainForm());
    }
}
