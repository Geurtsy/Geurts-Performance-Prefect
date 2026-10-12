using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GeurtsPerformancePrefect;

public static class ScrollbarVerification
{
    public static void Run(SettingsWindow panel, Action<bool, string> check)
    {
        var scroll = ((DockPanel)panel.Content).Children.OfType<ScrollViewer>().Single();
        panel.UpdateLayout();
        var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
        bar.ApplyTemplate();
        var track = (Track)bar.Template.FindName("PART_Track", bar);
        check(bar.IsVisible && bar.ActualWidth == 30 && track.Thumb.ActualWidth > 20,
            "The themed Settings scrollbar remains visible with a large drag target");

        void Move(double offset) { scroll.ScrollToVerticalOffset(offset); scroll.UpdateLayout(); }
        void Click(RepeatButton button)
        {
            var command = (System.Windows.Input.RoutedCommand)button.Command;
            check(command.CanExecute(button.CommandParameter, button), "Scrollbar navigation command is available");
            command.Execute(button.CommandParameter, button);
            scroll.UpdateLayout();
        }
        var middle = scroll.ScrollableHeight / 2;
        Move(middle);
        Click((RepeatButton)bar.Template.FindName("LineDown", bar));
        check(scroll.VerticalOffset > middle, "The themed down arrow scrolls Settings down");
        Move(middle);
        Click((RepeatButton)bar.Template.FindName("LineUp", bar));
        check(scroll.VerticalOffset < middle, "The themed up arrow scrolls Settings up");
        Move(middle);
        Click(track.IncreaseRepeatButton);
        check(scroll.VerticalOffset > middle + scroll.ViewportHeight / 2, "Clicking below the thumb scrolls a page down");
        Move(middle);
        Click(track.DecreaseRepeatButton);
        check(scroll.VerticalOffset < middle - scroll.ViewportHeight / 2, "Clicking above the thumb scrolls a page up");

        Move(middle);
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 30) { RoutedEvent = Thumb.DragDeltaEvent });
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 30, false) { RoutedEvent = Thumb.DragCompletedEvent });
        scroll.UpdateLayout();
        check(scroll.VerticalOffset > middle, "Dragging the themed thumb moves the Settings viewport");
        Move(scroll.ScrollableHeight);
        Click((RepeatButton)bar.Template.FindName("LineDown", bar));
        check(scroll.VerticalOffset == scroll.ScrollableHeight, "The themed scrollbar stays within the bottom boundary");
        Move(0);
        Click((RepeatButton)bar.Template.FindName("LineUp", bar));
        check(scroll.VerticalOffset == 0, "The themed scrollbar stays within the top boundary");
    }
}
