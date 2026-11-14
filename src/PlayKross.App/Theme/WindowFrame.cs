using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PlayKross.App.Theme;

/// <summary>Matches the Windows title bar to the PlayKross palette. Older Windows versions ignore these attributes.</summary>
public static class WindowFrame
{
    private const int UseImmersiveDarkMode = 20, CaptionColor = 35, TextColor = 36;
    // COLORREF is 0x00BBGGRR: navy #0E182A and text #EDF2F7.
    private const int Navy = 0x002A180E, Text = 0x00F7F2ED;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        window.Icon ??= new BitmapImage(new Uri("pack://application:,,,/Assets/playkross-logo.png"));
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != 0) Style(handle);
        else window.SourceInitialized += (_, _) => Style(new WindowInteropHelper(window).Handle);
    }

    private static void Style(nint handle)
    {
        int dark = 1, caption = Navy, text = Text;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
    }
}
