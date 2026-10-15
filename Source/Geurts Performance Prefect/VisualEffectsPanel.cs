using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public sealed class VisualEffectsPanel : StackPanel
{
    internal Button ApplyButton { get; } = new() { Content = "Adjust for best performance", HorizontalAlignment = HorizontalAlignment.Left };
    internal Button RestoreButton { get; } = new() { Content = "Restore previous appearance", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal TextBlock Status { get; } = Note("Changes apply only when you click a button.");
    readonly VisualEffectsService service;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    public VisualEffectsPanel() : this(VisualEffectsService.Create()) { }
    internal VisualEffectsPanel(VisualEffectsService service)
    {
        this.service = service;
        Children.Add(new TextBlock { Text = "Windows aesthetics", FontSize = 15, Foreground = Palette.Text,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,0) });
        Children.Add(Note("Reduce Windows visual effects for your account: animations, fades, shadows, font smoothing, thumbnail previews and window contents while dragging. Text edges and the desktop may look simpler."));
        Children.Add(ApplyButton); Children.Add(RestoreButton); Children.Add(Status);
        Children.Add(Note("Your previous appearance is saved before applying. Some desktop and taskbar effects take effect after you sign out and back in. Nothing is applied on startup."));
        RestoreButton.IsEnabled = service.HasBackup;
        ApplyButton.Click += (_, _) => Operation = Run(false);
        RestoreButton.Click += (_, _) => Operation = Run(true);
    }
    async Task Run(bool restore)
    {
        if (!ApplyButton.IsEnabled) return;
        ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
        Status.Text = restore ? "Restoring previous appearance…" : "Applying best performance…";
        try { Status.Text = await Task.Run(() => restore ? service.Restore() : service.Apply()); }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { ApplyButton.IsEnabled = true; RestoreButton.IsEnabled = service.HasBackup; }
    }
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0,7,0,7) };
}
