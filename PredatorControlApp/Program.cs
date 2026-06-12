namespace PredatorControlApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                MessageBox.Show($"Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "Predator Control — Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    MessageBox.Show($"Fatal: {ex.Message}\n\n{ex.StackTrace}",
                        "Predator Control — Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new Form1());
        }
    }
}