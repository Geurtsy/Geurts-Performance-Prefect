using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

public static class SliderThemeVerification
{
    public static void Run(SettingsWindow settings, string folder, Action<bool, string> check)
    {
        var sliders = settings.Sections.Items.Cast<TabItem>().SelectMany(tab =>
            ((Panel)((ScrollViewer)tab.Content).Content).Children.OfType<Slider>()).ToArray();
        check(sliders.Length == 4, "Slider theme covers averaging, battery CPU limit, opacity and size");
        foreach (var control in sliders)
        {
            SettingsTabVerification.Select(settings, control); control.ApplyTemplate();
            check(control.Template.FindName("PART_Track", control) is Track track && track.Thumb.ActualWidth >= 22,
                "Settings slider retains a usable themed drag handle: " + control.Minimum + "–" + control.Maximum);
        }
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, SmallChange = 5, LargeChange = 20, TickFrequency = 5, IsSnapToTickEnabled = true };
        var stack = new StackPanel { Margin = new Thickness(20), Background = Palette.Background };
        stack.Children.Add(new TextBlock { Text = "Adjustable", Foreground = Palette.Text }); stack.Children.Add(slider);
        foreach (var value in new[] { 0d, 100d, 50d })
        {
            stack.Children.Add(new TextBlock { Text = value == 50 ? "Unavailable" : value == 0 ? "Minimum" : "Maximum", Foreground = Palette.Muted, Margin = new Thickness(0,12,0,0) });
            stack.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = value, IsEnabled = value != 50 });
        }
        var window = new Window { Width = 430, Height = 350, Content = stack, ShowInTaskbar = false, Title = "Slider theme verification" };
        try
        {
            window.Show(); Pump();
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            void Click(RepeatButton button) => ((RoutedCommand)button.Command).Execute(button.CommandParameter, button);
            Click(track.IncreaseRepeatButton);
            check(slider.Value == 70, "Themed track click increases by the existing large step");
            Click(track.DecreaseRepeatButton);
            check(slider.Value == 50, "Themed track click decreases by the existing large step");
            slider.Focus();
            void KeyPress(Key key) => slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider), Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
            KeyPress(Key.Right);
            check(slider.Value == 55, "Themed slider retains keyboard small-step selection");
            KeyPress(Key.Home); check(slider.Value == 0, "Home reaches the slider minimum");
            KeyPress(Key.End); check(slider.Value == 100, "End reaches the slider maximum");
            slider.Value = 50; Pump();
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(50, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(50, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            check(slider.Value > 50 && slider.Value % 5 == 0, "Themed handle dragging changes the value and preserves tick snapping");
            slider.Value = 100; Click(track.IncreaseRepeatButton);
            check(slider.Value == 100, "Themed track respects the upper bound");
            slider.Value = 0; Click(track.DecreaseRepeatButton);
            check(slider.Value == 0, "Themed track respects the lower bound");
            slider.Value = 50; slider.IsEnabled = false;
            var rejected = false;
            try { ((IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue)).SetValue(80); }
            catch (ElementNotEnabledException) { rejected = true; }
            check(rejected && slider.Value == 50 && !track.Thumb.IsEnabled && !track.IncreaseRepeatButton.IsEnabled,
                "Disabled themed sliders reject accessible value changes and disable their mouse controls");
            slider.IsEnabled = true; slider.Focus(); Pump();
            foreach (var width in new[] { 430d, 330d })
            {
                window.Width = width; Pump();
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(stack), null, new Rect(stack.RenderSize));
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(stack.ActualWidth), (int)Math.Ceiling(stack.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(drawing);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, "sliders-" + width + ".png")); encoder.Save(stream);
            }
        }
        finally { window.Close(); }
    }
    static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
