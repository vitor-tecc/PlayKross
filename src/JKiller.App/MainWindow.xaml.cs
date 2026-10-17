using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using JKiller.Contracts;
using JKiller.Core.Settings;
using JKiller.Windows.Presentation;
using JKiller.Windows.Rooms;
namespace JKiller.App;
public partial class MainWindow : Window
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private MainWindowViewModel? model;
    private readonly JKiller.Windows.Viewing.PlaybackAudioGate playbackAudioGate = new();
    private RoomHttpClient? roomClient;
    private string root = "";
    private Window? viewingWindow;
    private CancellationTokenSource? command;
    private bool closing, closed, synchronizingAudio;
    public MainWindow()
    {
        InitializeComponent();
        root = Environment.CurrentDirectory;
        if (!File.Exists(Path.Combine(root, "config/dependencies.json")))
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "config/dependencies.json"))) { root = directory.FullName; break; }
        }
        roomClient = new(http); model = new(root, roomClient, playbackAudioGate); DataContext = model;
        model.PropertyChanged += (_, args) => Dispatcher.InvokeAsync(() =>
        {
            if (args.PropertyName == nameof(model.CaptureLive)) SetControls();
            if (args.PropertyName == nameof(model.Audio))
            {
                synchronizingAudio = true;
                try
                {
                    AudioModePicker.SelectedIndex = (int)model.Audio.Mode;
                    AudioSource.IsEnabled = model.Audio.Mode == AudioMode.Application;
                    AudioOutput.IsEnabled = model.Audio.Mode == AudioMode.SystemOutput;
                }
                finally { synchronizingAudio = false; }
            }
        });
    }
    private void SetControls()
    {
        ConnectionControls.IsEnabled = command is null;
        Link.IsReadOnly = model?.CaptureLive == true;
        JoinButton.IsEnabled = model?.CaptureLive != true;
        SelectionControls.IsEnabled = command is null && model?.CaptureLive != true;
        SettingsControls.IsEnabled = command is null; StartButton.IsEnabled = command is null && model?.CaptureLive != true;
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (command is not null || closing) return;
        using var cancellation = new CancellationTokenSource(); command = cancellation; SetControls();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { MessageBox.Show(this, error.Message, "PlayKross", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { command = null; SetControls(); }
    }
    private async void JoinClick(object sender, RoutedEventArgs args) => await Run(model!.JoinAsync);
    private async void LeaveClick(object sender, RoutedEventArgs args) => await Run(async token => { viewingWindow?.Close(); await model!.LeaveAsync(token); });
    private async void RenameClick(object sender, RoutedEventArgs args) => await Run(model!.RenameAsync);
    private async void RefreshClick(object sender, RoutedEventArgs args) => await Run(model!.RefreshAsync);
    private async void ExportClick(object sender, RoutedEventArgs args) => await Run(async token =>
    {
        var report = await model!.ExportDiagnosticsAsync(token);
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON report|*.json", FileName = "jkiller-diagnostics.json" };
        if (dialog.ShowDialog(this) == true) await File.WriteAllTextAsync(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), token);
    });
    private async void StartClick(object sender, RoutedEventArgs args) => await Run(token => { ReadSettings(); return model!.StartAsync(token); });
    private async void ApplyClick(object sender, RoutedEventArgs args) => await Run(token => { ReadSettings(); return model!.ApplySettingsAsync(token); });
    private async void StopClick(object sender, RoutedEventArgs args)
    {
        command?.Cancel();
        try { await model!.StopAsync(CancellationToken.None); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "PlayKross"); }
    }
    private void ReadSettings()
    {
        var height = int.Parse(((ComboBoxItem)Resolution.SelectedItem).Tag.ToString()!);
        var fps = int.Parse(((ComboBoxItem)Fps.SelectedItem).Tag.ToString()!);
        model!.Settings = StreamSettings.DefaultFor(height, fps) with { BitrateMbps = (decimal)Bitrate.Value };
    }
    private void PresetChanged(object sender, SelectionChangedEventArgs args)
    {
        if (Bitrate is null || Resolution?.SelectedItem is not ComboBoxItem resolution || Fps?.SelectedItem is not ComboBoxItem fps) return;
        Bitrate.Value = (double)StreamSettings.DefaultFor(int.Parse(resolution.Tag.ToString()!), int.Parse(fps.Tag.ToString()!)).BitrateMbps;
    }
    private void BitrateChanged(object sender, RoutedPropertyChangedEventArgs<double> args) { if (BitrateLabel is not null) BitrateLabel.Text = $"{args.NewValue:0.0} Mbps requested (megabits per second)"; }
    private void AudioModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (synchronizingAudio || model is null || AudioSource is null || AudioOutput is null) return;
        var mode = (AudioMode)AudioModePicker.SelectedIndex;
        AudioSource.IsEnabled = mode == AudioMode.Application; AudioOutput.IsEnabled = mode == AudioMode.SystemOutput;
        model.Audio = mode switch
        {
            AudioMode.Application => new(AudioMode.Application, (AudioSource.SelectedItem as SourceDescriptor)?.Process),
            AudioMode.SystemOutput => new(AudioMode.SystemOutput, RenderEndpointId: (AudioOutput.SelectedItem as AudioOutputDescriptor)?.Id),
            _ => AudioSelection.Off
        };
    }
    private void AudioChanged(object sender, SelectionChangedEventArgs args)
    {
        if (model is not null && AudioModePicker.SelectedIndex == (int)AudioMode.Application && AudioSource.SelectedItem is SourceDescriptor source) model.SelectApplicationAudio(source);
    }
    private void CopyClick(object sender, RoutedEventArgs args) { if (model!.RoomLink.Length > 0) Clipboard.SetText(model.RoomLink); }
    private void ViewClick(object sender, RoutedEventArgs args)
    {
        try { RoomHttpClient.ParseLink(model!.RoomLink); Process.Start(new ProcessStartInfo(model.RoomLink) { UseShellExecute = true }); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "PlayKross"); }
    }
    private async void WatchClick(object sender, RoutedEventArgs args) => await Run(async token =>
    {
        var parsed = new Uri(model!.RoomLink); viewingWindow?.Close();
        var viewer = new Viewing.RoomWebView(root, roomClient!, playbackAudioGate);
        var window = Viewing.RoomViewerWindow.Create(viewer); window.Owner = this; viewingWindow = window;
        window.Closed += (_, _) => { viewer.Dispose(); if (viewingWindow == window) viewingWindow = null; };
        window.Show();
        try { await viewer.OpenRoomAsync(parsed, token); } catch { window.Close(); throw; }
    });
    private async void WindowClosing(object? sender, CancelEventArgs args)
    {
        if (closed) return; args.Cancel = true; if (closing) return; closing = true; command?.Cancel();
        try { viewingWindow?.Close(); await model!.DisposeAsync(); }
        finally { http.Dispose(); closed = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
}
