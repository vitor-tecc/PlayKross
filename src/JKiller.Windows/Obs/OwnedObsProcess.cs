using System.Diagnostics;
using JKiller.Contracts;
using JKiller.Windows.Capture;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Globalization;

namespace JKiller.Windows.Obs;

public sealed record OwnedEngineHandle(ProcessIdentity Identity, string PipeName, ProcessIdentity Controller);
public interface IOwnedObsProcess
{
    Task<OwnedEngineHandle> StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class OwnedObsProcess(string repositoryRoot) : IOwnedObsProcess, IAsyncDisposable
{
    private readonly string root = Path.GetFullPath(repositoryRoot);
    private readonly SemaphoreSlim gate = new(1);
    private Process? process;
    private FileStream? ownershipLock;
    private OwnedEngineHandle? handle;
    private string RuntimeRoot => Path.Combine(root, ".runtime", "obs");

    public ProcessStartInfo BuildLaunch(string pipeName, ProcessIdentity controller)
    {
        var executable = Path.Combine(RuntimeRoot, "bin", "64bit", "obs64.exe");
        var launch = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "--portable", "--multi", "--profile", "JKiller", "--collection", "JKiller", "--minimize-to-tray", "--disable-updater", "--disable-missing-files-check" }) launch.ArgumentList.Add(argument);
        launch.Environment["JKILLER_CONTROL_PIPE"] = pipeName;
        launch.Environment["JKILLER_OWNER_PID"] = controller.Pid.ToString(CultureInfo.InvariantCulture);
        launch.Environment["JKILLER_OWNER_CREATION"] = controller.CreationFileTime.ToString(CultureInfo.InvariantCulture);
        launch.Environment["JKILLER_OWNER_IMAGE"] = controller.ExecutablePath;
        launch.Environment.Remove("OBS_PLUGINS_PATH");
        launch.Environment.Remove("OBS_PLUGINS_DATA_PATH");
        return launch;
    }

    public async Task<OwnedEngineHandle> StartAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (process is { HasExited: false } && handle is not null) return handle;
            Cleanup();
            var owner = ProcessIdentityReader.Read(Environment.ProcessId) ?? throw new InvalidOperationException("Controller identity unavailable.");
            if (!File.Exists(Path.Combine(RuntimeRoot, "bin", "64bit", "obs64.exe"))) throw new FileNotFoundException("Run scripts/Setup-Dev.ps1 and scripts/Build-Native.ps1 -Plugin first.");
            ownershipLock = new FileStream(Path.Combine(RuntimeRoot, "jkiller-owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            InitializeProfile();
            cancellationToken.ThrowIfCancellationRequested();
            var pipeName = "jkiller-" + Guid.NewGuid().ToString("N");
            process = Process.Start(BuildLaunch(pipeName, owner)) ?? throw new InvalidOperationException("Owned engine could not start.");
            var identity = ProcessIdentityReader.Read(process.Id) ?? throw new InvalidOperationException("Owned engine identity unavailable.");
            handle = new(identity, pipeName, owner);
            return handle;
        }
        catch { TerminateOwned(); Cleanup(); throw; }
        finally { gate.Release(); }
    }

    private void InitializeProfile()
    {
        var templates = Path.Combine(root, "config", "obs");
        var allowed = File.ReadAllLines(Path.Combine(templates, "allowed-plugins.txt")).Where(x => x.Length != 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plugins = Directory.GetFiles(Path.Combine(RuntimeRoot, "obs-plugins", "64bit"), "*.dll");
        if (plugins.Any(x => !allowed.Contains(Path.GetFileName(x))) || !plugins.Any(x => Path.GetFileName(x) == "jk-obs.dll"))
            throw new InvalidDataException("Owned OBS plugins differ from the pinned allowlist; restore the project runtime.");
        var config = Path.Combine(RuntimeRoot, "config", "obs-studio");
        Directory.CreateDirectory(config);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows user unavailable.");
        foreach (var sid in new[] { current, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(config).SetAccessControl(acl);
        // Always seed an empty scene and disabled devices, including after a controller crash.
        foreach (var relative in new[] { "global.ini", "user.ini", "basic/profiles/JKiller/basic.ini", "basic/scenes/JKiller.json", "plugin_config/obs-websocket/config.json" })
        {
            var destination = Path.Combine(config, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(templates, relative), destination, true);
        }
        ClearServiceSecrets();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cleanup must survive cancellation of the command that started publishing.
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            if (process is { HasExited: false })
            {
                process.CloseMainWindow();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)); }
                catch (TimeoutException) { TerminateOwned(); await process.WaitForExitAsync(); }
            }
        }
        finally { Cleanup(); gate.Release(); }
    }

    private void TerminateOwned() { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
    private void ClearServiceSecrets()
    {
        foreach (var name in new[] { "service.json", "service.json.bak", "service.json.tmp" })
            File.Delete(Path.Combine(RuntimeRoot, "config", "obs-studio", "basic", "profiles", "JKiller", name));
    }
    private void Cleanup()
    {
        process?.Dispose(); process = null; handle = null;
        if (ownershipLock is not null) { ClearServiceSecrets(); ownershipLock.Dispose(); ownershipLock = null; }
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
