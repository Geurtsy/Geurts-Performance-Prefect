using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;

namespace GeurtsPerformancePrefect;

// Hosts Windows' own Programs and Features Shell view in-process. No existing
// Control Panel/Explorer windows are discovered, reparented or terminated.
public sealed class ProgramsFeaturesPanel : DockPanel
{
    internal Button RetryButton { get; } = new() { Content = "Retry loading", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock Status { get; } = new() { Text = "Loading Programs and Features…", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) };
    internal ProgramsFeaturesHost Host { get; } = new();
    internal bool HasOpened { get; private set; }
    internal event Action? Opening;

    public ProgramsFeaturesPanel()
    {
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Programs and Features", Foreground = Palette.Text, FontSize = 16, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "The classic Windows uninstall interface. Select a program and use its Uninstall or Change action. Windows and the program control any confirmation or administrator prompts; these may open separately.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) });
        heading.Children.Add(RetryButton); heading.Children.Add(Status);
        SetDock(heading, Dock.Top); Children.Add(heading); Children.Add(Host);
        Host.Failed += message => { Status.Text = "Could not load Programs and Features: " + message; RetryButton.Visibility = Visibility.Visible; };
        RetryButton.Click += (_, _) => Open();
        // Visibility changes also cover returning to a tab whose HWND WPF recreated.
        IsVisibleChanged += (_, _) => { if (IsVisible) Dispatcher.InvokeAsync(() => { if (IsVisible) Open(); }); };
    }

    internal void Open()
    {
        HasOpened = true;
        Opening?.Invoke();
        if (Host.Open())
        {
            RetryButton.Visibility = Visibility.Collapsed;
            Status.Text = "Windows Programs and Features is embedded below. Right-click a program for its available actions. Uninstallers may continue in separate windows.";
        }
    }
}

internal sealed class ProgramsFeaturesHost : HwndHost
{
    // Microsoft.ProgramsAndFeatures, independent of the Windows display language.
    const string Location = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\::{7b81be6a-ce2b-4676-a29e-eb907a5126c5}";
    IExplorerBrowser? browser;
    nint container;
    bool requested;
    internal bool IsOpen => browser != null;
    internal event Action<string>? Failed;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        container = Native.CreateWindowEx(0, "static", "", 0x56000000, 0,0,1,1, parent.Handle,0,0,0);
        if (container == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (requested) Open();
        return new HandleRef(this, container);
    }

    internal bool Open()
    {
        requested = true;
        if (browser != null) return true;
        if (container == 0) return false;
        nint pidl = 0;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Location, 0, out pidl, 0, out _));
            browser = (IExplorerBrowser)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("71f96385-ddd6-48d3-a0c1-ae06e8b055fb"), true)!)!;
            Native.GetClientRect(container, out var rect);
            var settings = new FolderSettings { ViewMode = 4, Flags = 0x200000 }; // details, no web view
            browser.Initialize(container, ref rect, ref settings);
            // Navigate only to this applet, with native command and status panes;
            // do not save layout/selection changes into the user's Explorer preferences.
            browser.SetOptions(0x1 | 0x2 | 0x8 | 0x80); // NAVIGATEONCE | SHOWFRAMES | NOTRAVELLOG | NOPERSISTVIEWSTATE
            browser.SetPropertyBag("GeurtsPerformancePrefect.ProgramsAndFeatures");
            browser.BrowseToIDList(pidl, 0);
            return true;
        }
        catch (Exception ex) { CloseBrowser(); Failed?.Invoke(ex.Message); return false; }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }

    internal nint ViewWindow
    {
        get
        {
            if (browser == null) return 0;
            object? view = null;
            try
            {
                var iid = new Guid("000214E3-0000-0000-C000-000000000046"); // IShellView
                browser.GetCurrentView(ref iid, out view);
                ((IShellViewWindow)view).GetWindow(out var window); return window;
            }
            catch (COMException) { return 0; }
            finally { if (view != null) Marshal.ReleaseComObject(view); }
        }
    }

    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        if (browser != null && Native.GetClientRect(container, out var rect)) browser.SetRect(0, ref rect);
    }

    // Let the Shell view handle arrows, Tab and context-menu keys when focused.
    protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers)
    {
        if (browser is IInputObject input && input.TranslateAcceleratorIO(ref msg) == 0) return true;
        return base.TranslateAcceleratorCore(ref msg, modifiers);
    }

    void CloseBrowser()
    {
        if (browser == null) return;
        try { browser.Destroy(); }
        finally { Marshal.ReleaseComObject(browser); browser = null; }
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CloseBrowser(); Native.DestroyWindow(hwnd.Handle); container = 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);

    [StructLayout(LayoutKind.Sequential)] struct FolderSettings { internal uint ViewMode, Flags; }
    // Vtable order and signatures follow shobjidl_core.h, including unused slots.
    [ComImport, Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IExplorerBrowser
    {
        void Initialize(nint parent, ref Native.Rect rect, ref FolderSettings settings);
        void Destroy();
        void SetRect(nint deferred, ref Native.Rect rect);
        void SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string name);
        void SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFolderSettings(ref FolderSettings settings);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void BrowseToIDList(nint pidl, uint flags);
        void BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object target, uint flags);
        void FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object target, uint flags);
        void RemoveAll();
        void GetCurrentView(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object view);
    }
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellViewWindow { void GetWindow(out nint window); }
    [ComImport, Guid("68284FAA-6A48-11D0-8C78-00C04FD918B4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInputObject
    {
        [PreserveSig] int UIActivateIO([MarshalAs(UnmanagedType.Bool)] bool activate, ref MSG msg);
        [PreserveSig] int HasFocusIO();
        [PreserveSig] int TranslateAcceleratorIO(ref MSG msg);
    }
}

