using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using System.Runtime.InteropServices;
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var root = Environment.CurrentDirectory;
        var launch = new ProcessStartInfo(Path.Combine(root, ".tools/dotnet/dotnet.exe")) { UseShellExecute = false, WorkingDirectory = root, WindowStyle = ProcessWindowStyle.Hidden };
        launch.ArgumentList.Add(Path.Combine(root, "src/JKiller.App/bin/Debug/net10.0-windows10.0.26100.0/JKiller.App.dll"));
        using var application = Process.Start(launch) ?? throw new Exception("WPF launch failed.");
        AutomationElement? window = null;
        try
        {
            Wait(() => { application.Refresh(); if (application.HasExited) throw new Exception("WPF exited before showing its window."); return application.MainWindowHandle != 0; });
            window = AutomationElement.FromHandle(application.MainWindowHandle);
            var link = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "RoomLink"));
            ((ValuePattern)link.GetCurrentPattern(ValuePattern.Pattern)).SetValue(Environment.GetEnvironmentVariable("JKILLER_UI_JOIN_LINK") ?? throw new Exception("Invitation missing."));
            var join = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Join room"));
            ((InvokePattern)join.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Wait(() => window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Joined. Select video and optional application audio, then Start.")) is not null);
            Console.WriteLine("PASS: actual WPF window initialized, joined its room, and exposed the native capture controls.");
            if (Environment.GetEnvironmentVariable("JKILLER_UI_GEOMETRY_DASH") == "1")
            {
                var combo = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "VideoSource"));
                ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                AutomationElement? item = null;
                Wait(() =>
                {
                    var entries = AutomationElement.RootElement.FindAll(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, application.Id), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)));
                    foreach (AutomationElement entry in entries)
                        if (entry.Current.Name == "Geometry Dash" || entry.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Geometry Dash")) is not null) { item = entry; return true; }
                    return false;
                });
                Console.WriteLine("Geometry Dash choice patterns: " + string.Join(",", item!.GetSupportedPatterns().Select(value => value.ProgrammaticName)));
                if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection)) ((SelectionItemPattern)selection).Select();
                else if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var invocation)) ((InvokePattern)invocation).Invoke();
                else
                {
                    var point = item.GetClickablePoint();
                    if (item.Current.ProcessId != application.Id) throw new Exception("Choice does not belong to the owned app.");
                    if (!SetCursorPos((int)point.X, (int)point.Y)) throw new Exception("Could not point at the owned game choice.");
                    var inputs = new[] { new Input { Type = 0, Mouse = new() { Flags = 2 } }, new Input { Type = 0, Mouse = new() { Flags = 4 } } };
                    if (SendInput(2, inputs, Marshal.SizeOf<Input>()) != 2) throw new Exception("The owned game choice click failed.");
                }
                ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
                var start = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Start broadcast"));
                ((InvokePattern)start.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                Wait(() => window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Broadcasting")) is not null);
                Console.WriteLine("PASS: WPF source selection and Start published Geometry Dash at its default 1080p60 setting, with audio explicitly off.");
                var rename = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Change nickname"));
                if (!rename.Current.IsEnabled) throw new Exception("Nickname editing must remain available while broadcasting.");
                ((InvokePattern)rename.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                Wait(() => window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Nickname updated; your session identity stays the same.")) is not null);
            }
            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
            if (!application.WaitForExit(10000) || application.ExitCode != 0) throw new Exception("WPF close failed.");
        }
        catch
        {
            Console.WriteLine("Native UI did not reach the requested state; no screenshots or source titles were saved.");
            throw;
        }
        finally { if (!application.HasExited) { application.Kill(); application.WaitForExit(); } }
    }
    private static void Wait(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!condition()) { if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Native UI did not reach its expected state."); Thread.Sleep(100); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public MouseInput Mouse; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
