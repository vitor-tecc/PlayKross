using System.Windows;

namespace JKiller.App.Viewing;

public readonly record struct ViewerWindowMode(WindowState State, WindowStyle Style, ResizeMode Resize);

public sealed class ViewerFullscreenState
{
    private ViewerWindowMode? previous;
    public ViewerWindowMode Update(bool fullscreen, ViewerWindowMode current)
    {
        if (fullscreen)
        {
            previous ??= current;
            return current with { State = WindowState.Maximized, Style = WindowStyle.None };
        }
        var restored = previous ?? current;
        previous = null;
        return restored;
    }
}

public static class RoomViewerWindow
{
    public static Window Create(RoomWebView viewer)
    {
        var window = new Window { Title = "PlayKross • Room viewer", Width = 1000, Height = 740, Content = viewer };
        var state = new ViewerFullscreenState();
        viewer.FullscreenChanged += (_, _) =>
        {
            var mode = state.Update(viewer.IsFullscreen, new(window.WindowState, window.WindowStyle, window.ResizeMode));
            window.WindowStyle = mode.Style;
            window.ResizeMode = mode.Resize;
            window.WindowState = mode.State;
        };
        return window;
    }
}
