using System.Runtime.InteropServices;
using System.Text;

namespace ScalingDiag;

/// <summary>
/// Isolates two WinForms behaviours away from TypeyTypey. Both underpin rules in AGENTS.md §6;
/// re-run this before arguing that either rule is unnecessary.
///
/// Writes results to the console and to scaling-diag.txt beside the executable.
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Log = new();
    private static readonly Size Logical = new(615, 700);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    private const uint ModAlt = 0x0001, ModCtrl = 0x0002, ModNoRepeat = 0x4000;
    private const int HotkeyId = 0x5459;

    private static void Line(string text)
    {
        Log.AppendLine(text);
        Console.WriteLine(text);
    }

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        HandleLifetimeExperiment();
        AutoScaleExperiment();

        string path = Path.Combine(AppContext.BaseDirectory, "scaling-diag.txt");
        File.WriteAllText(path, Log.ToString());
        Line($"\nwritten to {path}");
    }

    /// <summary>
    /// Experiment 1. Hide() preserves a form's handle; ShowInTaskbar = false destroys and recreates
    /// it, taking any RegisterHotKey registration with it. This is the v1.0.3 hotkey defect.
    /// </summary>
    private static void HandleLifetimeExperiment()
    {
        Line("=== Experiment 1: handle lifetime ===");
        using var form = new Form { Text = "ScalingDiag", StartPosition = FormStartPosition.CenterScreen };

        form.Shown += (_, _) =>
        {
            IntPtr first = form.Handle;
            Line($"  handle after Shown         = 0x{first.ToInt64():X}");
            Line($"  RegisterHotKey(first)      = {RegisterHotKey(first, HotkeyId, ModCtrl | ModAlt | ModNoRepeat, (uint)Keys.V)}");

            form.Hide();
            IntPtr afterHide = form.Handle;
            Line($"  handle after Hide()        = 0x{afterHide.ToInt64():X}  changed={afterHide != first}");

            form.ShowInTaskbar = false;
            IntPtr afterTaskbar = form.Handle;
            Line($"  handle after ShowInTaskbar = 0x{afterTaskbar.ToInt64():X}  changed={afterTaskbar != afterHide}");
            Line($"  original still a window    = {IsWindow(first)}");
            Line($"  UnregisterHotKey(first)    = {UnregisterHotKey(first, HotkeyId)} (err {Marshal.GetLastWin32Error()}; 1400 = ERROR_INVALID_WINDOW_HANDLE)");
            Line("");
            Line($"  => Hide() is safe; ShowInTaskbar destroys the window: {afterTaskbar != afterHide && !IsWindow(first)}");
            Line("");
            form.Close();
        };

        form.ShowDialog();
    }

    /// <summary>
    /// Experiment 2. Assigning AutoScaleMode resets AutoScaleDimensions to the current device DPI,
    /// so a scale pass running immediately computes a factor of 1.0. Only a pass deferred to
    /// ResumeLayout(true) applies the real factor. This is the v1.0.3 window sizing defect.
    /// </summary>
    private static void AutoScaleExperiment()
    {
        Line("=== Experiment 2: autoscaling ===");

        Measure("A: AutoScale props + ClientSize in ctor, no suspend/resume", form =>
        {
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ClientSize = Logical;
        });

        Measure("B: same, wrapped in SuspendLayout / ResumeLayout(true)", form =>
        {
            form.SuspendLayout();
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ClientSize = Logical;
            form.ResumeLayout(performLayout: true);
        });

        Measure("C: AutoScaleMode.Font with a 6x13 baseline", form =>
        {
            form.SuspendLayout();
            form.AutoScaleDimensions = new SizeF(6F, 13F);
            form.AutoScaleMode = AutoScaleMode.Font;
            form.ClientSize = Logical;
            form.ResumeLayout(performLayout: true);
        });
    }

    private static void Measure(string label, Action<Form> configure)
    {
        using var form = new Form { Text = "ScalingDiag", StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "probe", AutoSize = true, Location = new Point(10, 10) });

        configure(form);

        form.Shown += (_, _) =>
        {
            float scale = form.DeviceDpi / 96f;
            int wantWidth = (int)(Logical.Width * scale);
            int wantHeight = (int)(Logical.Height * scale);
            bool correct = Math.Abs(form.ClientSize.Width - wantWidth) < 12;

            Line($"  {label}");
            Line($"     DeviceDpi={form.DeviceDpi} ({scale:0.00}x)  AutoScaleDimensions={form.AutoScaleDimensions}");
            Line($"     ClientSize={form.ClientSize}  want {wantWidth}x{wantHeight} for {Logical.Width}x{Logical.Height} logical");
            Line($"     => SCALED CORRECTLY: {correct}");
            Line("");
            form.Close();
        };

        form.ShowDialog();
    }
}
