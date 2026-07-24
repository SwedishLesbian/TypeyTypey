using System.Threading;

namespace TypeyTypey;

internal static class Program
{
    private const string MutexName = "TypeyTypey.SingleInstance.4D39A83C-52EC-4620-BB73-0265522DF85E";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TypeyTypey is already running.", "TypeyTypey",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
