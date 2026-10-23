using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;

// A same-user, same-session signal opens settings; it carries no command or path.
internal static class AssistantActivation
{
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    static void GrantForeground()
    {
        using(var caller=Process.GetCurrentProcess())
            foreach(var process in Process.GetProcessesByName("TaskbarMonitor"))
                using(process) try { if(process.SessionId==caller.SessionId) AllowSetForegroundWindow(process.Id); }
                catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
    }
    internal static string Name {
        get { using(var identity=WindowsIdentity.GetCurrent()) return "Local\\ZCWinAssistant.Show."+identity.User.Value; }
    }
    internal static EventWaitHandle Create()
    {
        var security=new EventWaitHandleSecurity();
        using(var identity=WindowsIdentity.GetCurrent())
            security.AddAccessRule(new EventWaitHandleAccessRule(identity.User,EventWaitHandleRights.Modify|EventWaitHandleRights.Synchronize,AccessControlType.Allow));
        bool created;
        return new EventWaitHandle(false,EventResetMode.AutoReset,Name,out created,security);
    }
    internal static bool Signal(int timeoutMs)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        do {
            try { using(var signal=EventWaitHandle.OpenExisting(Name,EventWaitHandleRights.Modify)) { GrantForeground(); signal.Set(); return true; } }
            catch(WaitHandleCannotBeOpenedException) { }
            if(timer.ElapsedMilliseconds>=timeoutMs) return false;
            Thread.Sleep(100);
        } while(true);
    }
}
