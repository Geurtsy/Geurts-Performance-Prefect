using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

// Informational only: closing never requires an acknowledgement or a second click.
internal sealed class ExitWarning : Window
{
    readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24),
        Foreground = Palette.Text, FontSize = 15 };
    internal const string Warning = "Closing Prefect…\n\nActive work is stopping automatically. Completed changes remain; interrupted checks or repairs may be incomplete. Session-controlled fans return to hardware automatic.\n\nPrefect will exit when cleanup finishes. Windows-managed background operations, such as BitLocker decryption, may continue.";
    internal ExitWarning()
    {
        Title = "Geurts Performance Prefect · Closing";
        Content = message; message.Text = Warning; Background = Palette.Background;
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; ShowInTaskbar = true;
    }
    internal void ReportFailure(string error)
    {
        message.Text = "Prefect could not finish closing: " + error + "\n\nRetry Exit after resolving the error.";
        WindowStyle = WindowStyle.SingleBorderWindow;
        if (!IsVisible) Show();
    }
}
