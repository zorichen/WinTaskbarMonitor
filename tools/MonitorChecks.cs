using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

// Build with the production sources and config; no installer, startup registration or fan writes.
internal static class MonitorChecks
{
    private static int checks;
    private static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception(message);
        checks++;
    }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if(args.Contains("--attached-menu")) {
                using(var sensors=new SensorService(1000,5000))
                using(var widget=new MonitorForm(sensors,new Settings(),false)) {
                    widget.Show(); Application.DoEvents();
                    var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                    var menu=(ContextMenuStrip)typeof(MonitorForm).GetField("menu",flags).GetValue(widget);
                    menu.Show(widget,new Point(0,0)); Application.DoEvents();
                    Check(menu.Font.Unit==GraphicsUnit.Pixel,"Attached popup uses pixel font.");
                    float popupScale=Native.GetDpiForWindow(menu.Handle)/96f;
                    Check(Math.Abs(menu.Font.Size/popupScale-SystemFonts.MenuFont.SizeInPoints*96f/72f)<0.1f,"Popup font matches Windows menu size with one DPI conversion.");
                    Rectangle previous=Rectangle.Empty;
                    foreach(ToolStripItem item in menu.Items) {
                        Check(menu.ClientRectangle.Contains(item.Bounds),"Attached menu item within popup.");
                        if(!previous.IsEmpty) Check(previous.Bottom<=item.Bounds.Top,"Attached menu rows never overlap.");
                        if(item is ToolStripMenuItem) {
                            var measured=TextRenderer.MeasureText(item.Text,item.Font);
                            Check(item.Height>=measured.Height,"Attached menu text fits row.");
                            Check(item.Width-item.Padding.Horizontal>=measured.Width,"Attached menu text fits padded width.");
                        }
                        previous=item.Bounds;
                    }
                    using(var image=new Bitmap(menu.Width,menu.Height)) { menu.DrawToBitmap(image,menu.ClientRectangle); image.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"attached-menu.png")); }
                    Console.WriteLine("ATTACHED nativeDpi="+Native.GetDpiForWindow(menu.Handle)+" managedDpi="+menu.DeviceDpi+" font="+menu.Font+" menu="+menu.Size+" checks="+checks);
                    menu.Close();
                }
                return 0;
            }
            if(args.Contains("--context-preview")) {
                string folder=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"context-preview");
                System.IO.Directory.CreateDirectory(folder);
                Environment.SetEnvironmentVariable("ENGLISH_INPUT_LOCK_DATA",System.IO.Path.Combine(folder,"input"));
                Program.QuitSignal=new EventWaitHandle(false,EventResetMode.AutoReset);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"show.request"),DateTime.UtcNow.ToString("o"));
                using(var sensors=new SensorService(1000,5000))
                using(var context=new MonitorContext(sensors,new Settings(),true,System.IO.Path.Combine(folder,"show.request"),folder)) Application.Run(context);
                return 0;
            }
            if(args.Contains("--assistant-preview")) {
                Environment.SetEnvironmentVariable("ENGLISH_INPUT_LOCK_DATA",System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assistant-preview-input"));
                using(var sensors=new SensorService(1000,5000))
                using(var assistant=new AssistantForm(sensors,new Settings())) {
                    assistant.ExitRequested+=delegate { Application.Exit(); };
                    assistant.ShowMain(0); Application.Run();
                }
                return 0;
            }
            if(EnglishInputLock.Tests.Run(new [] {"--self-test",System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"input-checks.txt")})!=0) throw new Exception("Input lock checks failed.");
            foreach(int dpi in new [] {96,144,216,288}) foreach(Size area in new [] {new Size(3840,2160),new Size(1280,720),new Size(640,480)}) {
                Size min=AssistantForm.FitSize(AssistantForm.MinimumLogicalSize,dpi,area);
                Size max=AssistantForm.FitSize(AssistantForm.MaximumLogicalSize,dpi,area);
                Size initial=AssistantForm.FitSize(AssistantForm.DefaultLogicalSize,dpi,area);
                Check(min.Width<=initial.Width && min.Height<=initial.Height,"Default must fit minimum.");
                Check(initial.Width<=max.Width && initial.Height<=max.Height,"Default must fit maximum.");
                Check(max.Width<=area.Width && max.Height<=area.Height,"Maximum must fit working area.");
            }
            if(args.Contains("--hardware")) { Probe(); return 0; }
            Check(GpuSelection.Choose(new GpuReading[0])==null,"No adapters must remain unavailable.");
            GpuReading intel=new GpuReading { Id="intel",Kind="GpuIntel",Temperature=80 };
            GpuReading nvidia=new GpuReading { Id="nvidia",Kind="GpuNvidia",Temperature=40 };
            Check(GpuSelection.Choose(new [] {intel,nvidia})==nvidia,"Prefer NVIDIA over Intel even when Intel is warmer.");
            Check(GpuSelection.Choose(new [] {intel})==intel,"Intel-only fallback.");
            GpuReading amd=new GpuReading { Id="amd",Kind="GpuAmd",Temperature=70 };
            Check(GpuSelection.Choose(new [] {intel,nvidia,amd})==amd,"Highest core temperature among preferred vendors.");
            nvidia.Temperature=null;
            Check(GpuSelection.Choose(new [] {intel,nvidia})==nvidia,"Missing dGPU temperature must not masquerade as Intel temperature.");
            Check(GpuSelection.IsCoreTemperature("GPU Core") && GpuSelection.IsCoreTemperature("GPU"),"Core sensor names.");
            Check(!GpuSelection.IsCoreTemperature("GPU Hot Spot") && !GpuSelection.IsCoreTemperature("GPU Memory Junction"),"Do not confuse hot spot or VRAM with core.");
            nvidia.Temperature=40;
            GpuReading second=new GpuReading { Id="nvidia-b",Kind="GpuNvidia",Temperature=40 };
            Check(GpuSelection.Choose(new [] {second,nvidia})==nvidia,"Equal readings must have stable selection.");
            Check(GpuSelection.Choose(new [] {intel,second})==second,"Re-enumerated adapter replaces removed adapter.");
            using(SensorService sensors=new SensorService(1000,5000))
            using(MonitorForm form=new MonitorForm(sensors,new Settings(),true))
            {
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                object gate=typeof(SensorService).GetField("gate",flags).GetValue(sensors);
                lock(gate)
                {
                    Readings cached=(Readings)typeof(SensorService).GetField("readings",flags).GetValue(sensors);
                    cached.Gpus=new [] {nvidia};
                    cached.GpuTemp=40; cached.GpuLoad=55; cached.CpuTemp=60; cached.DiskTemp=35;
                    cached.TemperatureAt=DateTime.UtcNow.AddMinutes(-1);
                    Readings stale=sensors.Snapshot();
                    Check(stale.GpuTemp==null && stale.GpuLoad==null,"Expired GPU summary must be unavailable.");
                    Check(stale.Gpus[0].Temperature==null && stale.Gpus[0].Load==null,"Expired per-adapter readings must be unavailable.");
                    Check(cached.Gpus[0].Temperature==40,"Snapshot expiry must not mutate the sampling cache.");
                    Check(stale.CpuTemp==null && stale.DiskTemp==null,"Existing temperature expiry must still work.");
                    cached.TemperatureAt=DateTime.UtcNow;
                    Check(sensors.Snapshot().GpuTemp==40,"Fresh GPU readings remain available.");
                }
                // Exercise actual WinForms layout, rather than a separately reimplemented formula.
                form.Show(); Application.DoEvents();
                Type type=typeof(MonitorForm);
                var timer=(System.Windows.Forms.Timer)type.GetField("timer",flags).GetValue(form);
                timer.Stop();
                foreach(int columns in new [] {2,3})
                foreach(float scale in new [] {1f,1.25f,1.5f,2.25f})
                {
                    type.GetField("columns",flags).SetValue(form,columns);
                    type.GetField("scale",flags).SetValue(form,scale);
                    type.GetField("values",flags).SetValue(form,Enumerable.Repeat("100°C",columns*2).ToArray());
                    type.GetMethod("RebuildFonts",flags).Invoke(form,null);
                    form.ClientSize=new Size((int)((columns==3?252:168)*scale),(int)(38*scale));
                    for(int i=0;i<columns*2;i++)
                    {
                        Rectangle cell=(Rectangle)type.GetMethod("Cell",flags).Invoke(form,new object[] {i});
                        Check(form.ClientRectangle.Contains(cell),"Cell outside widget at "+scale);
                    }
                    using(Bitmap bitmap=new Bitmap(form.Width,form.Height)) form.DrawToBitmap(bitmap,form.ClientRectangle);
                }
                var menu=(ContextMenuStrip)type.GetField("menu",flags).GetValue(form);
                bool inputEnabled=true;
                form.InputLockEnabled=()=>inputEnabled;
                form.InputLockToggled+=value=>inputEnabled=value;
                menu.Show(form,new Point(0,form.Height)); Application.DoEvents();
                Check(menu.Items.Count==6,"Four actions plus two separators.");
                Check(menu.Items[0].Text=="打开黑皮win助手" && menu.Items[5].Text=="退出黑皮win助手","Assistant menu entry and exit.");
                Check(((ToolStripMenuItem)menu.Items[3]).Checked,"Input switch reflects current state.");
                menu.Items[3].PerformClick(); Check(!inputEnabled,"Input menu switch controls shared state.");
                menu.Show(form,new Point(0,form.Height)); Application.DoEvents();
                Check(!((ToolStripMenuItem)menu.Items[3]).Checked,"Input switch refreshes after toggling.");
                foreach(ToolStripItem item in menu.Items)
                {
                    Check(item.Bounds.Width>0 && item.Bounds.Height>0,"Empty menu item.");
                    Check(menu.ClientRectangle.Contains(item.Bounds),"Clipped menu item.");
                    if(item is ToolStripMenuItem)
                    {
                        Size text=TextRenderer.MeasureText(item.Text,item.Font);
                        Check(item.Width>=text.Width && item.Height>=text.Height,"Clipped menu text.");
                    }
                }
                Console.WriteLine("Menu DeviceDpi="+menu.DeviceDpi+" Size="+menu.Size+" FontPoints="+menu.Font.SizeInPoints);
                using(var image=new Bitmap(menu.Width,menu.Height)) { menu.DrawToBitmap(image,menu.ClientRectangle); image.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assistant-menu-preview.png")); }
                menu.Close(); form.Hide();
            }
            Console.WriteLine("PASS "+checks+" assertions (layout scales simulated; menu uses current display DPI).");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Probe()
    {
        Console.WriteLine("IsAdmin="+Program.Admin);
        using(SensorService sensors=new SensorService(1000,5000))
        {
            Thread.Sleep(8000);
            Console.WriteLine("SAMPLER "+new JavaScriptSerializer().Serialize(sensors.Snapshot()));
            sensors.RefreshGpu();
            Thread.Sleep(6000);
            Console.WriteLine("RESCAN "+new JavaScriptSerializer().Serialize(sensors.Snapshot()));
        }
        // Read only: opening with default, empty settings leaves controls Undefined.
        Computer computer=new Computer { IsCpuEnabled=true,IsGpuEnabled=true,IsMotherboardEnabled=true };
        try
        {
            computer.Open();
            foreach(IHardware hardware in computer.Hardware) Inspect(hardware);
        }
        finally { computer.Close(); }
    }
    private static void Inspect(IHardware hardware)
    {
        hardware.Update();
        Console.WriteLine("HARDWARE "+hardware.HardwareType+" "+hardware.Name+" "+hardware.Identifier);
        foreach(ISensor sensor in hardware.Sensors.Where(s=>s.SensorType==SensorType.Fan || s.SensorType==SensorType.Control || s.SensorType==SensorType.Temperature))
            Console.WriteLine("SENSOR "+sensor.SensorType+" "+sensor.Name+" value="+sensor.Value+" control="+(sensor.Control==null?"none":sensor.Control.ControlMode+" min="+sensor.Control.MinSoftwareValue+" max="+sensor.Control.MaxSoftwareValue));
        foreach(IHardware child in hardware.SubHardware) Inspect(child);
    }
}
