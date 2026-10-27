using System.Diagnostics;

namespace FaldSync.UI;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        // Portable-build smoke test: load the self-contained WinForms runtime and exit
        // without touching DDC or the single-instance mutex.
        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            return;
        }

        _singleInstance = new Mutex(false, @"Local\AcerFALDv2.SingleInstance");
        try
        {
            try
            {
                if (!_singleInstance.WaitOne(0))
                {
                    MessageBox.Show(
                        "AcerFALDv2 is already running. Open it from the notification area.",
                        "AcerFALDv2",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous UI process ended abruptly; we now own the mutex.
            }

            // The original FaldSync UI controls the same DDC channel. Running both
            // makes them fight over brightness/mode writes. --allow-parallel is an
            // emergency/test escape hatch; it is not part of the normal workflow.
            var allowParallel = args.Any(a =>
                string.Equals(a, "--allow-parallel", StringComparison.OrdinalIgnoreCase));
            if (!allowParallel && Process.GetProcessesByName("FaldSync.UI").Length > 0)
            {
                MessageBox.Show(
                    "The original FaldSync UI is running.\r\n\r\n" +
                    "Close it before starting AcerFALDv2; both apps drive the same monitor.",
                    "AcerFALDv2",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ApplicationConfiguration.Initialize();
            using var form = new MainForm();
            Application.Run(form);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "AcerFALDv2 could not start.\r\n\r\n" + ex,
                "AcerFALDv2 error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try { _singleInstance.ReleaseMutex(); } catch { /* not owned */ }
            _singleInstance.Dispose();
        }
    }
}
