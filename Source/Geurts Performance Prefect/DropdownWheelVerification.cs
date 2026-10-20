using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GeurtsPerformancePrefect;

public static class DropdownWheelVerification
{
    public static void Run(SettingsWindow panel, Action<bool, string> check)
    {
        var choices = panel.Hud.Choices;
        panel.UpdateLayout();
        check(choices.Length == 5, "Wheel verification covers all five Settings dropdowns");
        foreach (var choice in choices)
        {
            var scroll = SettingsTabVerification.Select(panel, choice);
            choice.ApplyTemplate();
            var selected = choice.SelectedIndex;
            foreach (var delta in new[] { 120, -120 })
            {
                scroll.ScrollToVerticalOffset(scroll.ScrollableHeight / 2);
                scroll.UpdateLayout();
                var before = scroll.VerticalOffset;
                var forwarded = 0;
                MouseWheelEventHandler observe = (_, e) => { if (e.Delta == delta) forwarded++; };
                scroll.AddHandler(UIElement.MouseWheelEvent, observe, true);
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                    { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                choice.RaiseEvent(wheel);
                scroll.UpdateLayout();
                scroll.RemoveHandler(UIElement.MouseWheelEvent, observe);
                check(wheel.Handled && choice.SelectedIndex == selected && forwarded == 1 &&
                    (scroll.ScrollableHeight == 0 || (delta > 0 ? scroll.VerticalOffset < before : scroll.VerticalOffset > before)),
                    "Closed dropdown blocks wheel selection and forwards page scrolling: " + choices.ToList().IndexOf(choice) + "/" + delta);
            }
            choice.IsDropDownOpen = true;
            var openWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            choice.RaiseEvent(openWheel);
            check(!openWheel.Handled && choice.SelectedIndex == selected,
                "Open dropdown allows list scrolling without changing selection: " + choices.ToList().IndexOf(choice) + $" (open={choice.IsDropDownOpen}, handled={openWheel.Handled}, selected={choice.SelectedIndex}, expected={selected})");
            choice.IsDropDownOpen = false;
        }
    }
}
