namespace FaldSync.UI;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        _singleInstance = new Mutex(false, @"Local\FaldSync.UI.SingleInstance");
        try
        {
            try
            {
                if (!_singleInstance.WaitOne(0))
                {
                    MessageBox.Show(
                        "FaldSync is already running. Open it from the notification area.",
                        "FaldSync",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous UI process ended abruptly; we now own the mutex.
            }

            ApplicationConfiguration.Initialize();
            using var form = new MainForm();
            Application.Run(form);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "FaldSync could not start.\r\n\r\n" + ex,
                "FaldSync error",
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
