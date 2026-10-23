using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace EnglishInputLock
{
    public class Rule { public string Path = ""; public bool Enabled = true; }
    public class Settings { public bool Enabled = true; public List<Rule> Rules = new List<Rule>(); }

    static class Store
    {
        public static readonly string Folder = Environment.GetEnvironmentVariable("ENGLISH_INPUT_LOCK_DATA") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnglishInputLock");
        public static readonly string FileName = System.IO.Path.Combine(Folder, "settings.xml");
        public static Settings Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var data = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
                if (data.Rules == null) data.Rules = new List<Rule>();
                data.Rules = data.Rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Path)).ToList();
                return data;
            }
        }
        public static void Save(string path, Settings settings)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new XmlSerializer(typeof(Settings)).Serialize(stream, settings);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        public static bool Matches(string path, IEnumerable<Rule> rules)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return rules.Any(r => r.Enabled && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class WindowInfo
    {
        public IntPtr Window, Focus;
        public uint Pid, Thread;
        public string Path = "";
    }
    public interface IDesktop
    {
        WindowInfo Foreground();
        IntPtr Layout(uint thread);
        IntPtr EnglishLayout();
        bool StillForeground(IntPtr window);
        bool Request(WindowInfo window, IntPtr layout, out int error);
    }

    sealed class Desktop : IDesktop
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] layouts);
        [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct GuiInfo
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretRect;
        }
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        IntPtr cachedWindow;
        uint cachedPid;
        string cachedPath = "";
        DateTime cacheTime;
        public static string ProcessPath(uint pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return "";
            try
            {
                var buffer = new StringBuilder(32768); uint length = (uint)buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : "";
            }
            finally { CloseHandle(handle); }
        }
        public WindowInfo Foreground()
        {
            IntPtr window = GetForegroundWindow(); uint pid;
            uint thread = GetWindowThreadProcessId(window, out pid);
            if (window == IntPtr.Zero || thread == 0) return null;
            if (cachedWindow != window || cachedPid != pid || (DateTime.UtcNow - cacheTime).TotalSeconds > 5)
            {
                cachedWindow = window; cachedPid = pid; cachedPath = ProcessPath(pid); cacheTime = DateTime.UtcNow;
            }
            var gui = new GuiInfo { Size = (uint)Marshal.SizeOf(typeof(GuiInfo)) };
            IntPtr focus = window;
            if (GetGUIThreadInfo(thread, ref gui) && gui.Focus != IntPtr.Zero)
            {
                uint focusPid; uint focusThread = GetWindowThreadProcessId(gui.Focus, out focusPid);
                if (focusPid == pid) { focus = gui.Focus; thread = focusThread; }
            }
            return new WindowInfo { Window = window, Focus = focus, Pid = pid, Thread = thread, Path = cachedPath };
        }
        public IntPtr Layout(uint thread) { return GetKeyboardLayout(thread); }
        public IntPtr EnglishLayout()
        {
            int count = GetKeyboardLayoutList(0, null);
            var items = new IntPtr[Math.Max(1, count)];
            int actual = GetKeyboardLayoutList(items.Length, items);
            for (int i = 0; i < actual; i++)
                if ((items[i].ToInt64() & 0xffffffffL) == 0x04090409L) return items[i];
            return IntPtr.Zero;
        }
        public bool StillForeground(IntPtr window) { return GetForegroundWindow() == window; }
        public bool Request(WindowInfo window, IntPtr layout, out int error)
        {
            // Only the selected foreground application's focused window; never broadcast.
            bool result = PostMessage(window.Focus, 0x0050, IntPtr.Zero, layout);
            error = result ? 0 : Marshal.GetLastWin32Error(); return result;
        }
    }

    public sealed class Engine
    {
        readonly IDesktop desktop;
        readonly Func<long> now;
        IntPtr activeWindow, activeFocus;
        uint activePid;
        long lastRequest = -10000, firstRequest = -1;
        public string State = "等待指定程序", Target = "", LayoutHex = "";
        public int Requests, Error;
        public Engine(IDesktop desktop, Func<long> now) { this.desktop = desktop; this.now = now; }
        void Reset() { activeWindow = IntPtr.Zero; activeFocus = IntPtr.Zero; activePid = 0; lastRequest = -10000; firstRequest = -1; Error = 0; }
        public void Tick(Settings settings)
        {
            Target = ""; LayoutHex = "";
            if (!settings.Enabled) { Reset(); State = "已暂停"; return; }
            WindowInfo info = desktop.Foreground();
            if (info == null || !Store.Matches(info.Path, settings.Rules)) { Reset(); State = "等待指定程序 · 其他软件不受影响"; return; }
            if (info.Window != activeWindow || info.Focus != activeFocus || info.Pid != activePid)
            {
                Reset(); activeWindow = info.Window; activeFocus = info.Focus; activePid = info.Pid;
            }
            Target = System.IO.Path.GetFileName(info.Path);
            IntPtr wanted = desktop.EnglishLayout();
            if (wanted == IntPtr.Zero) { State = "未找到美式键盘 · 请先在 Windows 添加"; return; }
            IntPtr current = desktop.Layout(info.Thread);
            LayoutHex = current.ToInt64().ToString("X16");
            if (current == wanted) { State = "英文锁定中 · " + Target; firstRequest = -1; Error = 0; return; }
            long time = now();
            if (time - lastRequest >= 250 && desktop.StillForeground(info.Window))
            {
                int error; bool ok = desktop.Request(info, wanted, out error);
                Error = error; lastRequest = time; Requests++;
                if (firstRequest < 0) firstRequest = time;
                if (!ok) { State = "切换被阻止（错误 " + error + "）· " + Target; return; }
            }
            State = Error != 0 ? "切换被阻止（错误 " + Error + "）· " + Target :
                firstRequest >= 0 && time - firstRequest > 2000 ? "程序未响应切换 · " + Target : "正在切换为英文 · " + Target;
        }
    }

}
