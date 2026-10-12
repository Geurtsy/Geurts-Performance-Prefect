using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

public sealed class BoringApplicationsPanel : StackPanel
{
    readonly OverlaySettings settings;
    readonly Func<bool> save;
    readonly Func<IReadOnlyList<BoringApplication>> discover;
    readonly BoringApplications service;
    readonly StackPanel entries = new();
    readonly TextBlock status = Note("Load installed applications to choose which ones to close.");
    readonly TextBlock count = Note("");
    readonly Button refresh = new() { Content = "Refresh installed applications", HorizontalAlignment = HorizontalAlignment.Left };
    readonly Button add = new() { Content = "Add executable…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    readonly TextBox search = new() { Margin = new Thickness(0,8,0,8), Padding = new Thickness(7),
        Background = Palette.Background, Foreground = Palette.Text, CaretBrush = Palette.Accent, BorderBrush = Palette.Muted };
    readonly CheckBox selectedOnly = new() { Content = "Show selected applications only" };
    readonly List<BoringApplication> applications = new();
    bool loaded, busy;
    public Button CloseButton { get; } = new() { Content = "Close Boring Applications", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,10,0,0) };
    internal string Status => status.Text;
    internal IEnumerable<CheckBox> Choices => entries.Children.OfType<CheckBox>();
    public BoringApplicationsPanel(OverlaySettings settings, Func<bool> save,
        Func<IReadOnlyList<BoringApplication>>? discover = null, BoringApplications? service = null)
    {
        this.settings = settings; this.save = save;
        TextBlock.SetForeground(this, Palette.Text);
        this.discover = discover ?? BoringApplications.Discover; this.service = service ?? new();
        Children.Add(new TextBlock { Text = "Close Boring Applications", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,0) });
        Children.Add(Note("Choose applications below, then close their running processes with one click. Forced closure can lose unsaved work. Nothing closes automatically."));
        Children.Add(CloseButton);
        Children.Add(count);
        Children.Add(status);
        Children.Add(refresh); Children.Add(add);
        Children.Add(Note("Search installed applications")); Children.Add(search); Children.Add(selectedOnly);
        System.Windows.Automation.AutomationProperties.SetName(search, "Search installed applications");
        Children.Add(new ScrollViewer { Content = entries, MaxHeight = 220, Style = (Style)Application.Current.FindResource("SettingsScrollViewer"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0,5,0,5) });
        Children.Add(Note("Desktop apps are loaded from Windows' installed-app list and Start menu. Apps without a detected executable cannot be selected; use Add executable for these, portable apps or Store apps. Windows components, other sign-in sessions and Prefect are protected. Apps may restart themselves."));
        refresh.Click += async (_, _) => await RefreshAsync();
        add.Click += (_, _) => AddExecutable();
        search.TextChanged += (_, _) => Render();
        selectedOnly.Checked += (_, _) => Render(); selectedOnly.Unchecked += (_, _) => Render();
        CloseButton.Click += async (_, _) => await CloseAsync();
        Loaded += async (_, _) => { if (!loaded) { loaded = true; await RefreshAsync(); } };
        Render();
    }
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0,7,0,7) };
    void SetBusy(bool value)
    {
        busy = value; refresh.IsEnabled = add.IsEnabled = entries.IsEnabled = !value;
        CloseButton.IsEnabled = !value && settings.BoringApplications.Count > 0;
    }
    internal async Task RefreshAsync()
    {
        if (busy) return;
        SetBusy(true); status.Text = "Loading installed applications…";
        try
        {
            var result = await Task.Run(discover);
            applications.Clear(); applications.AddRange(result);
            status.Text = $"Loaded {result.Count} installed application(s). Your saved selection is kept.";
            Render();
        }
        catch (Exception ex) { status.Text = "Could not load installed applications: " + ex.Message; }
        finally { SetBusy(false); }
    }
    void Render()
    {
        entries.Children.Clear();
        var all = applications.Concat(settings.BoringApplications).GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        int visible = 0;
        foreach (var app in all)
        {
            var saved = settings.BoringApplications.FirstOrDefault(a => a.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
            bool chosen = saved != null;
            if (!app.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || selectedOnly.IsChecked == true && !chosen) continue;
            visible++;
            var label = app.Name + (app.Executables.Length == 0 ? " · executable unavailable" : "");
            var toggle = new CheckBox { Tag = app.Id, Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                IsChecked = chosen, IsEnabled = chosen || app.Executables.Length > 0, Margin = new Thickness(0,5,0,5),
                ToolTip = saved != null ? "Saved executable targets:\n" + string.Join("\n", saved.Executables) :
                    app.Executables.Length > 0 ? string.Join("\n", app.Executables) : "Use Add executable to choose this application's .exe file." };
            System.Windows.Automation.AutomationProperties.SetName(toggle, label);
            toggle.Checked += (_, _) => Select(app, true);
            toggle.Unchecked += (_, _) => Select(app, false);
            entries.Children.Add(toggle);
        }
        count.Text = $"{settings.BoringApplications.Count} selected · {visible} shown. Selections save automatically.";
        CloseButton.IsEnabled = !busy && settings.BoringApplications.Count > 0;
        if (visible == 0) entries.Children.Add(Note("No applications match this view."));
    }
    void Select(BoringApplication app, bool selected)
    {
        bool restoreFocus = entries.IsKeyboardFocusWithin;
        var before = settings.BoringApplications.ToList();
        settings.BoringApplications.RemoveAll(a => a.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
        if (selected) settings.BoringApplications.Add(app);
        if (!save()) { settings.BoringApplications = before; status.Text = "Could not save the selection. The previous selection was restored."; }
        else status.Text = "Selection saved. Ready to close the selected applications.";
        Render();
        if (restoreFocus) (Choices.FirstOrDefault(c => (string)c.Tag == app.Id) ?? Choices.FirstOrDefault(c => c.IsEnabled))?.Focus();
    }
    void AddExecutable()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = BoringApplications.ExecutablePath(dialog.FileName);
        if (path == null || BoringApplications.Protected(path)) { status.Text = "This executable is protected and cannot be added."; return; }
        var app = new BoringApplication("file:" + path, Path.GetFileNameWithoutExtension(path), new[] { path });
        Select(app, true);
    }
    internal async Task CloseAsync()
    {
        if (busy || settings.BoringApplications.Count == 0) return;
        // Use a fixed, explicitly saved list. Refresh never silently broadens the user's selection.
        var selected = settings.BoringApplications.ToArray();
        SetBusy(true); status.Text = "Closing selected application processes…";
        try { status.Text = (await Task.Run(() => service.Close(selected))).Summary; }
        catch (Exception ex) { status.Text = "Could not close applications: " + ex.Message; }
        finally { SetBusy(false); }
    }
}
