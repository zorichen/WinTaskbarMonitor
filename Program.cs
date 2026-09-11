using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class Settings
{
    public int LoadIntervalMs=1000, TemperatureIntervalMs=5000, LeftOffset=0;
    public int WeatherWidth=160;
    public static readonly string FilePath=Path.Combine(Program.Home,"settings.json");
    public static Settings Load()
    {
        Settings settings=new Settings();
        try { if(File.Exists(FilePath)) settings=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath)) ?? settings; } catch {}
        settings.LoadIntervalMs=Math.Max(1000,Math.Min(10000,settings.LoadIntervalMs));
        settings.TemperatureIntervalMs=Math.Max(5000,Math.Min(60000,settings.TemperatureIntervalMs));
        settings.LeftOffset=Math.Max(-1000,Math.Min(1000,settings.LeftOffset));
        settings.WeatherWidth=Math.Max(0,Math.Min(400,settings.WeatherWidth));
        return settings;
    }
    public void Save() { File.WriteAllText(FilePath,new JavaScriptSerializer().Serialize(this)); }
}

internal static class Program
{
    internal static bool Admin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
    internal static readonly string AppDirectory=AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string Home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TaskbarMonitor");
    internal static bool CapturePreview;
    internal static EventWaitHandle QuitSignal;
    [STAThread] private static void Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Home);
            CapturePreview=args.Contains("--capture-preview");
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch {}
            if(args.Contains("--quit")) { try { using(var signal=EventWaitHandle.OpenExisting("Local\\ZoriTaskbarMonitorQuit")) signal.Set(); } catch(WaitHandleCannotBeOpenedException) {} return; }
            if(args.Contains("--diagnose")) { Diagnose(); return; }
            bool created;
            using(Mutex mutex=new Mutex(true,"Local\\ZoriTaskbarMonitor",out created))
            {
                if(!created) return;
                QuitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ZoriTaskbarMonitorQuit");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using(SensorService sensors=new SensorService(Settings.Load().LoadIntervalMs,Settings.Load().TemperatureIntervalMs))
                    Application.Run(new MonitorContext(sensors,Settings.Load(),args.Contains("--preview")));
                QuitSignal.Dispose();
            }
        }
        catch(Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Home,"error.log"),ex.ToString()); } catch {}
            MessageBox.Show("小组件启动失败：\n"+ex.Message,"任务栏监控",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
    private static void Diagnose()
    {
        using(SensorService sensors=new SensorService(1000,5000))
        {
            Thread.Sleep(7000);
            IntPtr taskbar=Native.FindWindow("Shell_TrayWnd",null);
            Native.Rect rect; Native.GetWindowRect(taskbar,out rect);
            object output=new { Time=DateTime.Now, IsAdmin=Admin, Readings=sensors.Snapshot(), Taskbar=rect, Dpi=taskbar==IntPtr.Zero?0:Native.GetDpiForWindow(taskbar), Elements=FindTaskbarElements(taskbar) };
            File.WriteAllText(Path.Combine(Home,"diagnostics.json"),new JavaScriptSerializer().Serialize(output));
        }
    }
    internal static object[] FindTaskbarElements(IntPtr handle)
    {
        if(handle==IntPtr.Zero) return new object[0];
        try
        {
            AutomationElement root=AutomationElement.FromHandle(handle);
            AutomationElementCollection elements=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            return elements.Cast<AutomationElement>().Where(e=>e.Current.AutomationId=="WidgetsButton" || e.Current.AutomationId=="StartButton").Select(e => {
                var p=e.Current; var r=p.BoundingRectangle;
                return (object)new { p.AutomationId, Left=r.Left,Top=r.Top,Right=r.Right,Bottom=r.Bottom };
            }).ToArray();
        }
        catch(Exception ex) { return new object[] { new { Error=ex.Message } }; }
    }
}

internal sealed class MonitorContext : ApplicationContext
{
    private MonitorForm form;
    private readonly SensorService sensors;
    private readonly Settings settings;
    private readonly bool preview;
    private readonly System.Windows.Forms.Timer watcher=new System.Windows.Forms.Timer();
    internal MonitorContext(SensorService sensors,Settings settings,bool preview)
    {
        this.sensors=sensors; this.settings=settings; this.preview=preview;
        CreateForm();
        watcher.Interval=1000;
        watcher.Tick+=delegate {
            if(Program.QuitSignal.WaitOne(0)) { ExitThread(); return; }
            if(form.IsDisposed || !form.IsHandleCreated || !Native.IsWindow(form.Handle)) { if(!form.IsDisposed) form.Dispose(); CreateForm(); }
        };
        watcher.Start();
    }
    private void CreateForm()
    {
        form=new MonitorForm(sensors,settings,preview);
        form.ExitRequested+=delegate { ExitThread(); };
        form.Show();
    }
    protected override void ExitThreadCore() { watcher.Stop(); if(form!=null) form.Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing) { if(disposing) { watcher.Dispose(); if(form!=null) form.Dispose(); } base.Dispose(disposing); }
}

internal sealed class MonitorForm : Form
{
    internal event Action ExitRequested;
    private readonly SensorService sensors;
    private readonly Settings settings;
    private readonly bool preview;
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    private readonly ToolTip tooltip=new ToolTip();
    private readonly NotifyIcon tray=new NotifyIcon();
    private readonly Icon appIcon;
    private readonly ContextMenuStrip menu=new ContextMenuStrip();
    private Font labelFont,valueFont;
    private Color labelColor,valueColor;
    private string[] values={"—","—","—","—"};
    private IntPtr taskbar;
    private float scale=1;
    private bool light,closing;
    private volatile bool locating;
    private volatile int detectedWeatherRight=-1;
    private int ticks;
    private Rectangle placed;
    private string placementSource="正在定位";
    private readonly DateTime started=DateTime.UtcNow;
    private readonly double startedCpu=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;

    public MonitorForm(SensorService sensors,Settings settings,bool preview)
    {
        this.sensors=sensors; this.settings=settings; this.preview=preview;
        Text="WinTaskbarMonitor · 任务栏监控"; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false;
        AutoScaleMode=AutoScaleMode.None; DoubleBuffered=true; StartPosition=FormStartPosition.Manual;
        Size=new Size(164,40); Location=new Point(200,200);
        using(Stream iconStream=typeof(Program).Assembly.GetManifestResourceStream("TaskbarMonitor.AppIcon"))
            appIcon=new Icon(iconStream,SystemInformation.SmallIconSize);
        Icon=appIcon;
        BuildMenu(); ContextMenuStrip=menu;
        tray.Icon=appIcon; tray.Text="任务栏监控"; tray.ContextMenuStrip=menu; tray.Visible=true;
        tray.DoubleClick+=delegate { ShowDetails(); };
        MouseDoubleClick+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) ShowDetails(); };
        MouseDown+=delegate(object sender,MouseEventArgs e) { if(preview && e.Button==MouseButtons.Left) { Native.ReleaseCapture(); Native.SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
        timer.Interval=1000; timer.Tick+=delegate { Tick(); }; timer.Start();
        Shown+=delegate { UpdateTheme(true); Place(); Tick(); };
        FormClosing+=delegate { closing=true; timer.Stop(); };
        SystemEvents.UserPreferenceChanged+=PreferencesChanged;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08000000|0x00000080; return p; } }
    private void PreferencesChanged(object sender,UserPreferenceChangedEventArgs e)
    {
        if(!closing && IsHandleCreated) try { BeginInvoke((Action)delegate { UpdateTheme(true); Place(); }); } catch(InvalidOperationException) {}
    }
    private void BuildMenu()
    {
        menu.Items.Add("查看读数和资源占用",null,delegate { ShowDetails(); });
        menu.Items.Add("保存诊断报告",null,delegate { SaveStatus(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("向左微调 4 像素",null,delegate { settings.LeftOffset-=4; settings.Save(); Place(); });
        menu.Items.Add("向右微调 4 像素",null,delegate { settings.LeftOffset+=4; settings.Save(); Place(); });
        menu.Items.Add("重置位置",null,delegate { settings.LeftOffset=0; settings.Save(); detectedWeatherRight=-1; LocateWeather(); Place(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出",null,delegate { if(ExitRequested!=null) ExitRequested(); });
    }
    private void UpdateTheme(bool force)
    {
        bool nextLight=false;
        using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            nextLight=Convert.ToInt32(key==null?0:key.GetValue("SystemUsesLightTheme",0))!=0;
        if(!force && nextLight==light) return;
        light=nextLight;
        // A nearly neutral color key preserves clean anti-aliased text edges on the taskbar.
        BackColor=light?Color.FromArgb(238,238,239):Color.FromArgb(40,40,41);
        TransparencyKey=preview?Color.Empty:BackColor;
        labelColor=light?Color.FromArgb(85,92,101):Color.FromArgb(185,193,203);
        valueColor=light?Color.FromArgb(27,32,39):Color.FromArgb(245,246,248);
        RebuildFonts(); Invalidate();
    }
    private void RebuildFonts()
    {
        if(labelFont!=null) labelFont.Dispose(); if(valueFont!=null) valueFont.Dispose();
        labelFont=new Font("Segoe UI",12*scale,FontStyle.Regular,GraphicsUnit.Pixel);
        valueFont=new Font("Segoe UI",12*scale,FontStyle.Regular,GraphicsUnit.Pixel);
    }
    private void LocateWeather()
    {
        if(locating || taskbar==IntPtr.Zero || preview) return;
        locating=true; IntPtr target=taskbar;
        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                Native.Rect bar; if(!Native.GetWindowRect(target,out bar)) return;
                AutomationElement root=AutomationElement.FromHandle(target);
                var buttons=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
                foreach(AutomationElement button in buttons)
                {
                    var p=button.Current;
                    string name=(p.Name+" "+p.AutomationId).ToLowerInvariant();
                    var bounds=p.BoundingRectangle;
                    if((name.Contains("widget") || name.Contains("小组件") || name.Contains("天气")) && !bounds.IsEmpty && bounds.Left<bar.Left+bar.Width/3 && bounds.Width>0)
                    { detectedWeatherRight=(int)Math.Ceiling(bounds.Right-bar.Left); return; }
                }
                detectedWeatherRight=-1;
            }
            catch { detectedWeatherRight=-1; }
            finally { locating=false; }
        });
    }
    private void Place()
    {
        if(preview) return;
        IntPtr current=Native.FindWindow("Shell_TrayWnd",null);
        if(current==IntPtr.Zero) { if(Visible) Hide(); return; }
        Native.Rect rect; if(!Native.GetWindowRect(current,out rect) || rect.Width<rect.Height) { Hide(); return; }
        bool changed=current!=taskbar;
        if(changed)
        {
            taskbar=current; detectedWeatherRight=-1;
            int style=Native.GetWindowLong(Handle,-16);
            Native.SetWindowLong(Handle,-16,(style & unchecked((int)~0x80000000))|0x40000000|0x04000000);
            Native.SetParent(Handle,taskbar);
            LocateWeather();
        }
        float dpi=Native.GetDpiForWindow(taskbar)/96f;
        if(dpi<1) dpi=1;
        if(Math.Abs(dpi-scale)>0.01) { scale=dpi; RebuildFonts(); }
        int width=(int)(168*scale),height=(int)(38*scale);
        int weather=detectedWeatherRight;
        if(weather<0) { weather=(int)(settings.WeatherWidth*scale); placementSource="预留天气宽度（可右键微调）"; }
        else placementSource="天气按钮边界";
        int x=Math.Max(0,weather+(int)((6+settings.LeftOffset)*scale));
        int y=Math.Max(0,(rect.Height-height)/2);
        Rectangle desired=new Rectangle(x,y,width,height);
        if(desired.Right>rect.Width) { Hide(); return; }
        if(changed || desired!=placed || !Visible)
        {
            if(!Visible) Show();
            Native.SetWindowPos(Handle,IntPtr.Zero,x,y,width,height,0x0010|0x0040);
            placed=desired;
        }
    }
    private static string Format(double? value,string unit) { return value.HasValue?Math.Round(value.Value).ToString("0")+unit:"—"; }
    private void Tick()
    {
        if(closing) return;
        Place();
        if(++ticks%30==0) { LocateWeather(); UpdateTheme(false); SaveStatus(); }
        Readings reading=sensors.Snapshot();
        string[] next={Format(reading.Cpu,"%"),Format(reading.Memory,"%"),Format(reading.CpuTemp,"°C"),Format(reading.DiskTemp,"°C")};
        for(int index=0;index<4;index++) if(next[index]!=values[index]) { values[index]=next[index]; Invalidate(Cell(index)); }
        string hint="CPU："+next[0]+"　内存："+next[1]+"\nCPU："+next[2]+"　硬盘："+next[3]+"\n"+reading.CpuSensor+"\n"+reading.DiskSensor;
        if(!string.IsNullOrEmpty(reading.Error)) hint+="\n"+reading.Error;
        tooltip.SetToolTip(this,hint);
        tray.Text=("CPU "+next[0]+" 内存 "+next[1]+"\nCPU "+next[2]+" 硬盘 "+next[3]);
        if(ticks==3) { SaveStatus(); if(Program.CapturePreview) using(Bitmap image=new Bitmap(Width,Height)) { DrawToBitmap(image,new Rectangle(0,0,Width,Height)); image.Save(Path.Combine(Program.Home,"widget-preview.png"),System.Drawing.Imaging.ImageFormat.Png); } }
    }
    private Rectangle Cell(int index)
    {
        int col=index%2,row=index/2;
        return new Rectangle((int)((col==0?4:96)*scale),(int)(row*19*scale),(int)(68*scale),(int)(19*scale));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if(labelFont==null) return;
        e.Graphics.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        string[] labels={"CPU","内存","CPU","硬盘"};
        using(Brush labelsBrush=new SolidBrush(labelColor)) using(Brush valuesBrush=new SolidBrush(valueColor))
        using(StringFormat left=new StringFormat(StringFormat.GenericTypographic))
        using(StringFormat valueFormat=new StringFormat(StringFormat.GenericTypographic))
        {
            left.LineAlignment=valueFormat.LineAlignment=StringAlignment.Center;
            left.FormatFlags=valueFormat.FormatFlags=StringFormatFlags.NoWrap;
            for(int i=0;i<4;i++)
            {
                Rectangle cell=Cell(i);
                // Keep each label/value pair tight and reserve the wider gap between columns.
                RectangleF valueBounds=new RectangleF(cell.X+30*scale,cell.Y,38*scale,cell.Height);
                e.Graphics.DrawString(labels[i],labelFont,labelsBrush,cell,left);
                e.Graphics.DrawString(values[i],valueFont,valuesBrush,valueBounds,valueFormat);
            }
        }
    }
    private object Status()
    {
        using(Process process=Process.GetCurrentProcess())
        {
            double elapsed=(DateTime.UtcNow-started).TotalMilliseconds;
            return new { Timestamp=DateTime.Now, ProcessId=process.Id, IsAdmin=Program.Admin, Readings=sensors.Snapshot(), LoadIntervalMs=settings.LoadIntervalMs, TemperatureIntervalMs=settings.TemperatureIntervalMs, WorkingSetMiB=process.WorkingSet64/1048576.0, PrivateMemoryMiB=process.PrivateMemorySize64/1048576.0, CpuPercentSinceStart=elapsed<=0?0:(process.TotalProcessorTime.TotalMilliseconds-startedCpu)/elapsed/Environment.ProcessorCount*100, LogicalProcessors=Environment.ProcessorCount, TaskbarHandle=taskbar.ToInt64(), ActualParent=Native.GetParent(Handle).ToInt64(), Bounds=placed, Scale=scale, PlacementSource=placementSource };
        }
    }
    private void SaveStatus() { try { File.WriteAllText(Path.Combine(Program.Home,"status.json"),new JavaScriptSerializer().Serialize(Status())); } catch {} }
    private void ShowDetails()
    {
        Readings r=sensors.Snapshot(); SaveStatus();
        using(Process p=Process.GetCurrentProcess())
            MessageBox.Show("CPU 占用 "+Format(r.Cpu,"%")+"    内存占用 "+Format(r.Memory,"%")+"\nCPU 温度 "+Format(r.CpuTemp,"°C")+"    硬盘温度 "+Format(r.DiskTemp,"°C")+"\n\n"+r.CpuSensor+"\n"+r.DiskSensor+"\n\n占用率间隔："+settings.LoadIntervalMs+" ms\n温度间隔："+settings.TemperatureIntervalMs+" ms\n小组件工作集："+(p.WorkingSet64/1048576.0).ToString("F1")+" MiB\n\n"+r.Error,"任务栏监控",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing) { SystemEvents.UserPreferenceChanged-=PreferencesChanged; timer.Dispose(); tray.Visible=false; tray.Dispose(); if(appIcon!=null) appIcon.Dispose(); tooltip.Dispose(); menu.Dispose(); if(labelFont!=null) labelFont.Dispose(); if(valueFont!=null) valueFont.Dispose(); }
        base.Dispose(disposing);
    }
}
