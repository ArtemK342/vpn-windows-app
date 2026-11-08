using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace FsocietyNet.Services;

/// <summary>
/// The downloaded exe is its own installer: run from anywhere else than the installed folder it offers to install
/// (folder, consents, shortcuts); «Apps → FsocietyNet → Uninstall» runs it with --uninstall. No separate setup
/// program: one ~100 MB file with .NET inside instead of a setup that carries a second copy of the runtime.
/// </summary>
public static class Installer
{
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FsocietyNet";
    public const string ExeName = "FsocietyNet.exe";

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FsocietyNet");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "FsocietyNet.lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "FsocietyNet.lnk");

    static RegistryKey Hklm => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    /// <summary>Installed copy: its folder and version (from «Apps & features»), or null.</summary>
    public static (string dir, string version)? Existing()
    {
        try
        {
            using var k = Hklm.OpenSubKey(UninstallKey);
            if (k?.GetValue("InstallLocation") is not string dir || !File.Exists(Path.Combine(dir, ExeName))) return null;
            return (dir.TrimEnd('\\'), k.GetValue("DisplayVersion") as string ?? "");
        }
        catch { return null; }
    }

    public static bool RunningInstalled
    {
        get
        {
            var here = Path.GetDirectoryName(Environment.ProcessPath ?? "")?.TrimEnd('\\') ?? "";
            return Existing() is { } e && here.Equals(e.dir, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Show the installer: release build started from the download (not the installed folder).</summary>
    public static bool ShouldOffer(string[] args)
    {
#if DEBUG || PREVIEW
        if (!args.Contains("--setup")) return false;
#endif
        if (args.Contains("--portable") || args.Contains("--tray") || args.Contains("--restarted")) return false;
        return !RunningInstalled;
    }

    /// <summary>A folder picked by the user gets its own «FsocietyNet» subfolder (uninstall deletes only that).</summary>
    public static string Normalize(string chosen)
    {
        var d = chosen.Trim().TrimEnd('\\');
        if (d.Length == 0) return DefaultDir;
        return Path.GetFileName(d).Equals("FsocietyNet", StringComparison.OrdinalIgnoreCase) ? d : Path.Combine(d, "FsocietyNet");
    }

    /// <summary>
    /// The app runs as admin: its folder must not be writable by ordinary programs (else one could swap the exe).
    /// Administrators / SYSTEM — full, users — read and run.
    /// </summary>
    static void Secure(string dir)
    {
        try
        {
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            new DirectoryInfo(dir).SetAccessControl(sec);
        }
        catch (Exception e) { Log.E("install acl", e); }
    }

    /// <summary>The installed app (if running) quits — it disconnects the VPN on the way out; older builds are closed.</summary>
    public static async Task StopRunning()
    {
        bool Others() => Process.GetProcessesByName("FsocietyNet").Any(p => p.Id != Environment.ProcessId);
        if (!Others()) return;
        try { if (EventWaitHandle.TryOpenExisting(@"Local\FsocietyNet.Quit", out var quit)) { quit.Set(); quit.Dispose(); } } catch { }
        for (int i = 0; i < 30 && Others(); i++) await Task.Delay(500);
        foreach (var p in Process.GetProcessesByName("FsocietyNet").Where(p => p.Id != Environment.ProcessId))
            try { p.Kill(true); p.WaitForExit(5000); } catch { }
    }

    public static async Task<string> Install(string dir, bool desktopShortcut, bool autostart)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        await StopRunning();
        Directory.CreateDirectory(dir);
        Secure(dir);
        var exe = Path.Combine(dir, ExeName);
        for (int i = 0; ; i++)
        {
            try { File.Copy(self, exe, true); break; }
            catch (IOException) when (i < 20) { await Task.Delay(500); }   // the old exe is released a moment after exit
        }

        Shortcut(StartMenuLink, exe);
        if (desktopShortcut) Shortcut(DesktopLink, exe);
        else try { File.Delete(DesktopLink); } catch { }

        using (var k = Hklm.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "FsocietyNet");
            k.SetValue("DisplayVersion", App.Version);
            k.SetValue("Publisher", "fsociety");
            k.SetValue("DisplayIcon", $"\"{exe}\",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            k.SetValue("URLInfoAbout", Api.Site);
            k.SetValue("HelpLink", Api.Site + "/faq");
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        AppSettings.Current.Autostart = autostart;
        AppSettings.Save();
        await Autostart.Set(autostart, exe);
        Log.I($"installed {App.Version} to {dir}");
        return exe;
    }

    static void Shortcut(string link, string exe)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var s = shell.CreateShortcut(link);
            s.TargetPath = exe;
            s.WorkingDirectory = Path.GetDirectoryName(exe);
            s.IconLocation = exe + ",0";
            s.Description = "FsocietyNet VPN";
            s.Save();
        }
        catch (Exception e) { Log.E("shortcut", e); }
    }

    /// <summary>
    /// Everything the app put on the PC: tunnel service, sing-box / ciadpi, our zapret service, system proxy, autostart
    /// task, shortcuts, «Apps» entry, %ProgramData%\FsocietyNet and (optionally) the settings and sign-in in %AppData%.
    /// The folder with the running exe is deleted by cmd a few seconds after exit.
    /// </summary>
    public static async Task Uninstall(bool removeUserData)
    {
        await StopRunning();
        try { await AwgEngine.Cleanup(); } catch { }
        Proc.KillOurs("sing-box.exe");
        Proc.KillOurs("ciadpi.exe");
        try
        {
            // only a zapret service of ours (a zapret installed by its own service.bat stays)
            using var svc = Hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret");
            if ((svc?.GetValue("ImagePath") as string ?? "").Contains(Paths.MachineDir, StringComparison.OrdinalIgnoreCase))
                await Zapret.RemoveServices();
            Zapret.KillOrphans();
        }
        catch (Exception e) { Log.E("uninstall zapret", e); }
        SystemProxy.Restore();
        await Autostart.Set(false);
        foreach (var l in new[] { StartMenuLink, DesktopLink }) try { File.Delete(l); } catch { }

        var dir = Existing()?.dir ?? Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
        try { Hklm.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { Directory.Delete(Paths.MachineDir, true); } catch (Exception e) { Log.E("uninstall programdata", e); }
        if (removeUserData) try { Directory.Delete(Paths.UserDir, true); } catch { }

        // The running exe cannot delete itself: cmd removes the folder after this process has exited
        if (dir.Length > 3 && Path.GetFileName(dir).Equals("FsocietyNet", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{dir}\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
            Process.Start(psi);
        }
    }
}
