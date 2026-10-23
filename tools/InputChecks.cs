using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace EnglishInputLock
{
    static class Tests
    {
        sealed class Fake : IDesktop
        {
            public WindowInfo Current = new WindowInfo { Window = new IntPtr(1), Focus = new IntPtr(1), Thread = 1, Pid = 7, Path = @"C:\Games\game.exe" };
            public IntPtr Value = new IntPtr(0x08040804), English = new IntPtr(0x04090409);
            public bool ForegroundUnchanged = true, Allow = true, Apply = true;
            public int Calls;
            public WindowInfo Foreground() { return Current; }
            public IntPtr Layout(uint thread) { return Value; }
            public IntPtr EnglishLayout() { return English; }
            public bool StillForeground(IntPtr window) { return ForegroundUnchanged; }
            public bool Request(WindowInfo window, IntPtr layout, out int error)
            { Calls++; error = Allow ? 0 : 5; if (Allow && Apply) Value = layout; return Allow; }
        }
        static void Check(bool result, string label, List<string> output)
        { if (!result) throw new Exception(label); output.Add("PASS " + label); }
        public static int Run(string[] args)
        {
            var output = new List<string>(); string temp = Path.Combine(args.Length > 1 ? Path.GetDirectoryName(Path.GetFullPath(args[1])) : AppDomain.CurrentDomain.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
            try
            {
                var fake = new Fake(); long now = 0;
                var config = new Settings(); config.Rules.Add(new Rule { Path = @"C:\Games\game.exe" });
                var engine = new Engine(fake, () => now);
                engine.Tick(config); Check(fake.Calls == 1 && fake.Value == fake.English, "matched Chinese layout requested English", output);
                Check(!engine.State.StartsWith("英文锁定中"), "request is not reported as verified", output);
                now = 100; engine.Tick(config); Check(engine.State.StartsWith("英文锁定中"), "readback confirms English", output);
                fake.Value = new IntPtr(0x08040804); now = 400; engine.Tick(config);
                Check(fake.Calls == 2 && fake.Value == fake.English, "manual Chinese switch corrected again", output);
                fake.Current.Path = @"C:\Other\game.exe"; fake.Value = new IntPtr(0x08040804); now = 800; engine.Tick(config);
                Check(fake.Calls == 2 && fake.Value.ToInt64() == 0x08040804, "same filename at another path untouched", output);
                fake.Current.Path = @"c:\games\GAME.exe"; now = 1200; engine.Tick(config); Check(fake.Calls == 3, "path matching is case insensitive", output);
                config.Enabled = false; fake.Value = new IntPtr(0x08040804); now = 1600; engine.Tick(config); Check(fake.Calls == 3, "pause does not switch", output);
                config.Enabled = true; config.Rules[0].Enabled = false; now = 2000; engine.Tick(config); Check(fake.Calls == 3, "disabled rule does not switch", output);
                config.Rules[0].Enabled = true; fake.ForegroundUnchanged = false; now = 2400; engine.Tick(config); Check(fake.Calls == 3, "focus race does not switch", output);
                fake.ForegroundUnchanged = true; fake.Allow = false; now = 2800; engine.Tick(config); Check(engine.Error == 5 && engine.State.Contains("被阻止"), "UIPI failure displayed", output);
                int count = fake.Calls; now = 2850; engine.Tick(config); Check(fake.Calls == count, "retry rate limited", output);
                fake.Allow = true; fake.Apply = false; now = 6000; engine.Tick(config); Check(engine.State.Contains("未响应"), "ignored request never reported as success", output);
                fake.English = IntPtr.Zero; now = 6400; count = fake.Calls; engine.Tick(config); Check(fake.Calls == count && engine.State.Contains("未找到"), "missing keyboard no invalid request", output);
                Directory.CreateDirectory(temp); string file = Path.Combine(temp, "settings.xml"); Store.Save(file, config);
                var restored = Store.Read(file); Check(restored.Rules.Count == 1 && restored.Rules[0].Path == config.Rules[0].Path, "config round trip", output);
                restored.Enabled = false; Store.Save(file, restored); Check(File.Exists(file + ".bak") && Store.Read(file + ".bak").Enabled, "atomic save retains previous config", output);
                File.WriteAllText(file, "<broken>"); bool rejected = false;
                try { Store.Read(file); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && File.ReadAllText(file) == "<broken>", "corrupt config remains untouched", output);
                output.Add("ALL " + output.Count + " CHECKS PASSED");
                if (args.Length > 1) File.WriteAllLines(args[1], output, Encoding.UTF8);
                return 0;
            }
            catch (Exception ex)
            {
                output.Add("FAIL " + ex.Message); if (args.Length > 1) File.WriteAllLines(args[1], output, Encoding.UTF8); return 1;
            }
            // Temporary fixtures are intentionally retained for diagnosis.
        }

        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        public static int Window(string[] args)
        {
            string folder = args[Array.IndexOf(args, "--test-window") + 1]; Directory.CreateDirectory(folder);
            Application.EnableVisualStyles();
            var form = new Form { Text = Path.GetFileNameWithoutExtension(Application.ExecutablePath) + " · 输入法验收窗口", Size = new Size(480, 200), StartPosition = FormStartPosition.CenterScreen };
            form.Controls.Add(new Label { Text = "自动验收用空白窗口，不读取或输入个人内容。", Dock = DockStyle.Top, Height = 50 });
            var box = new TextBox { Dock = DockStyle.Bottom }; form.Controls.Add(box);
            string previous = ""; var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += delegate
            {
                // Test windows explicitly cooperate when transferring focus to each other.
                if (GetForegroundWindow() == form.Handle) AllowSetForegroundWindow(0xffffffff);
                string commandPath = Path.Combine(folder, "command.txt");
                try
                {
                    if (File.Exists(commandPath))
                    {
                        string command = File.ReadAllText(commandPath);
                        if (command != previous)
                        {
                            previous = command;
                            if (command.Contains("close")) { form.Close(); return; }
                            if (command.Contains("focus")) { form.Activate(); SetForegroundWindow(form.Handle); box.Focus(); }
                            if (command.Contains("chinese"))
                            {
                                foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
                                    if (language.Culture.LCID == 0x0804) { InputLanguage.CurrentInputLanguage = language; break; }
                            }
                        }
                    }
                    File.WriteAllText(Path.Combine(folder, "state.txt"), "layout=" + GetKeyboardLayout(GetCurrentThreadId()).ToInt64().ToString("X16") + "\nforeground=" + (GetForegroundWindow() == form.Handle).ToString() + "\nwindow=" + form.Handle.ToInt64() + "\ncommand=" + previous.Trim(), Encoding.UTF8);
                }
                catch (IOException) { }
            };
            form.FormClosed += delegate { timer.Dispose(); }; timer.Start(); Application.Run(form); return 0;
        }
    }
}
