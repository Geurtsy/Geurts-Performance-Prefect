using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

public static class DropdownThemeVerification
{
    public static void Run(string folder, Action<bool, string> check)
    {
        var named = new ComboBox { DisplayMemberPath = "Name", ItemsSource = new[] { new { Name = "Automatic graphics card" }, new { Name = "Dedicated graphics card" } }, SelectedIndex = 0 };
        var choice = new ComboBox { MaxDropDownHeight = 220, Margin = new Thickness(0,16,0,0) };
        choice.Items.Add(new ComboBoxItem { Content = "Windows image: Check health (DISM)" });
        choice.Items.Add(new ComboBoxItem { Content = "Unavailable choice", IsEnabled = false });
        foreach (var index in Enumerable.Range(1, 20)) choice.Items.Add("Example drive or sensor " + index);
        choice.SelectedIndex = 0;
        var disabled = new ComboBox { ItemsSource = new[] { "Unavailable device" }, SelectedIndex = 0, IsEnabled = false, Margin = new Thickness(0,16,0,0) };
        var stack = new StackPanel { Margin = new Thickness(20), Background = Palette.Background };
        stack.Children.Add(named); stack.Children.Add(choice); stack.Children.Add(disabled);
        var window = new Window { Width = 430, Height = 270, Content = stack, ShowInTaskbar = false, Title = "Dropdown theme verification" };
        try
        {
            window.Show(); Pump();
            var selection = (ContentPresenter)named.Template.FindName("Selection", named);
            Capture(stack, Path.Combine(folder, "dropdowns-initial.png"));
            check(named.Text == "Automatic graphics card",
                "Themed dropdown preserves DisplayMemberPath labels: " + named.Text + " / " + selection.Content);
            named.Focus();
            named.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(named), Environment.TickCount, Key.Down) { RoutedEvent = Keyboard.KeyDownEvent });
            check(named.SelectedIndex == 1, "Themed closed dropdown retains keyboard selection");
            Capture(stack, Path.Combine(folder, "dropdowns-closed.png"));
            var toggle = (ToggleButton)choice.Template.FindName("DropDownToggle", choice);
            ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle(); Pump();
            var popup = (Popup)choice.Template.FindName("PART_Popup", choice);
            var scroll = (ScrollViewer)choice.Template.FindName("DropDownScroll", choice);
            check(choice.IsDropDownOpen && popup.IsOpen && popup.Child.IsVisible,
                "Themed dropdown arrow opens its list");
            check(scroll.ScrollableHeight > 0 && ((FrameworkElement)popup.Child).ActualWidth <= choice.ActualWidth + 1,
                "Long dropdown lists stay within the field width and scroll");
            var first = (ComboBoxItem)choice.ItemContainerGenerator.ContainerFromIndex(0);
            check(first.IsSelected && !((ComboBoxItem)choice.ItemContainerGenerator.ContainerFromIndex(1)).IsEnabled,
                "Themed list retains selected and disabled items");
            Capture((FrameworkElement)popup.Child, Path.Combine(folder, "dropdown-open.png"));
            scroll.ScrollToBottom(); Pump();
            check(scroll.VerticalOffset > 0, "Themed dropdown list scrolls to its final choices");
            Capture((FrameworkElement)popup.Child, Path.Combine(folder, "dropdown-scrolled.png"));
            choice.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(choice), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            Pump();
            check(!choice.IsDropDownOpen && choice.SelectedIndex == 0, "Escape closes the themed list without changing selection");
        }
        finally { window.Close(); }
    }

    static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    static void Capture(FrameworkElement content, string path)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
