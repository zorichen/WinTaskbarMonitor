using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class StartupRegistration
{
    public string UserSid, TaskName, ShortcutPath;
}

internal static class StartupProgram
{
    private static readonly string DirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string MonitorPath = Path.Combine(DirectoryPath, "TaskbarMonitor.exe");
    private static readonly string LauncherPath = Path.Combine(DirectoryPath, "TaskbarMonitorStartup.exe");
    private static readonly string RegistrationPath = Path.Combine(DirectoryPath, "startup-registration.json");
    private static readonly string InstallId = GetInstallId();
    private static readonly string ShortcutName = "TaskbarMonitor-" + InstallId + ".lnk";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--install-startup") Install();
            else if (args.Length == 1 && args[0] == "--remove-startup") Remove();
            else if (args.Length == 0) Launch();
            else throw new ArgumentException("Use --install-startup, --remove-startup, or no arguments to launch.");
            Log("Success: " + (args.Length == 0 ? "launch" : args[0]));
            return 0;
        }
        catch (Exception error)
        {
            Log(error.ToString());
            return 1;
        }
    }

    private static string GetInstallId()
    {
        string path = Path.GetFullPath(MonitorPath).ToUpperInvariant();
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "").Substring(0, 16);
    }

    private static string TaskName(string sid) { return "TaskbarMonitor-" + InstallId + "-" + new SecurityIdentifier(sid).Value; }
    private static bool SamePath(string left, string right) { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }

    private static void RequireAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Configure startup from the elevated installer or an administrator terminal.");
    }

    private static StartupRegistration ReadRegistration()
    {
        if (!File.Exists(RegistrationPath)) return null;
        StartupRegistration registration = new JavaScriptSerializer().Deserialize<StartupRegistration>(File.ReadAllText(RegistrationPath));
        if (registration == null || registration.TaskName != TaskName(registration.UserSid)
            || !Path.IsPathRooted(registration.ShortcutPath) || Path.GetFileName(registration.ShortcutPath) != ShortcutName)
            throw new InvalidDataException("Invalid startup registration; no task or shortcut was changed.");
        return registration;
    }

    private static dynamic Scheduler()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
        service.Connect();
        return service;
    }

    private static dynamic FindTask(dynamic folder, string name)
    {
        try { return folder.GetTask(name); }
        catch (FileNotFoundException) { return null; }
        catch (COMException error)
        {
            if (error.ErrorCode == unchecked((int)0x80070002)) return null;
            throw;
        }
    }

    private static void CheckTask(dynamic task)
    {
        if (task == null) return;
        dynamic actions = task.Definition.Actions;
        if (actions.Count != 1 || actions.Item(1).Type != 0 || !SamePath((string)actions.Item(1).Path, MonitorPath)
            || !string.IsNullOrEmpty((string)actions.Item(1).Arguments))
            throw new InvalidOperationException("A task with this name belongs to another command; it was not changed.");
    }

    private static void CheckShortcut(string path)
    {
        if (!File.Exists(path)) return;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        dynamic shortcut = shell.CreateShortcut(path);
        if (!SamePath((string)shortcut.TargetPath, LauncherPath) || !string.IsNullOrEmpty((string)shortcut.Arguments))
            throw new InvalidOperationException("The startup shortcut belongs to another command; it was not changed.");
    }

    private static void Install()
    {
        RequireAdministrator();
        if (!File.Exists(MonitorPath)) throw new FileNotFoundException("TaskbarMonitor.exe must be beside the launcher.", MonitorPath);
        string sid;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
        StartupRegistration previous = ReadRegistration();
        if (previous != null && previous.UserSid != sid)
            throw new InvalidOperationException("Startup is registered to another Windows account. Remove that registration before changing accounts.");
        string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrEmpty(startup)) throw new InvalidOperationException("The current account has no Startup folder.");
        StartupRegistration registration = previous ?? new StartupRegistration {
            UserSid = sid, TaskName = TaskName(sid), ShortcutPath = Path.Combine(startup, ShortcutName)
        };
        CheckShortcut(registration.ShortcutPath);
        dynamic service = Scheduler();
        dynamic folder = service.GetFolder("\\");
        CheckTask(FindTask(folder, registration.TaskName));
        // Persist the cleanup information before creating either Windows startup resource.
        File.WriteAllText(RegistrationPath, new JavaScriptSerializer().Serialize(registration));
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "WinTaskbarMonitor on-demand temperature monitor. Controlled by Task Manager > Startup apps.";
        definition.Principal.UserId = sid;
        definition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN; no saved password.
        definition.Principal.RunLevel = 1; // TASK_RUNLEVEL_HIGHEST.
        definition.Settings.Enabled = true;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = 2; // TASK_INSTANCES_IGNORE_NEW.
        dynamic action = definition.Actions.Create(0);
        action.Path = MonitorPath;
        action.WorkingDirectory = DirectoryPath;
        // No logon trigger: disabling the Startup-folder entry must prevent automatic launch.
        folder.RegisterTaskDefinition(registration.TaskName, definition, 6, sid, null, 3, null);
        Directory.CreateDirectory(Path.GetDirectoryName(registration.ShortcutPath));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        dynamic shortcut = shell.CreateShortcut(registration.ShortcutPath);
        shortcut.TargetPath = LauncherPath;
        shortcut.WorkingDirectory = DirectoryPath;
        shortcut.IconLocation = LauncherPath + ",0";
        shortcut.Description = "WinTaskbarMonitor";
        shortcut.Save();
    }

    private static void Remove()
    {
        RequireAdministrator();
        StartupRegistration registration = ReadRegistration();
        if (registration == null) return;
        dynamic folder = Scheduler().GetFolder("\\");
        dynamic task = FindTask(folder, registration.TaskName);
        CheckTask(task);
        CheckShortcut(registration.ShortcutPath);
        if (task != null) folder.DeleteTask(registration.TaskName, 0);
        if (File.Exists(registration.ShortcutPath)) File.Delete(registration.ShortcutPath);
        File.Delete(RegistrationPath);
    }

    private static void Launch()
    {
        StartupRegistration registration = ReadRegistration();
        if (registration == null) throw new InvalidOperationException("Startup has not been configured for this installation.");
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            if (identity.User.Value != registration.UserSid) throw new UnauthorizedAccessException("Startup belongs to another Windows account.");
        Thread.Sleep(15000);
        dynamic task = FindTask(Scheduler().GetFolder("\\"), registration.TaskName);
        if (task == null) throw new InvalidOperationException("The startup task is missing. Re-run the installer to repair it.");
        CheckTask(task);
        task.Run(null);
    }

    private static void Log(string message)
    {
        try
        {
            string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarMonitor");
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "startup-launcher.log"), DateTime.Now.ToString("o") + "\r\n" + message);
        }
        catch { } // Logging must not display a dialog or prevent a login session.
    }
}
