using System.Windows;

namespace Plantagotchi;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Create a detailed error message
        string errorMessage = $"An unhandled exception occurred that was not caught by other error handlers.\n\n" +
                              $"Error: {e.Exception.Message}\n\n" +
                              $"Call Stack (Stack Trace):\n{e.Exception.StackTrace}";

        // Display it to the user
        MessageBox.Show(errorMessage, "Global Error Handler", MessageBoxButton.OK, MessageBoxImage.Error);

        // Prevent the application from shutting down immediately.
        // This is useful for debugging, but in production, it might be necessary to shut down the app.
        e.Handled = true;
    }
}
