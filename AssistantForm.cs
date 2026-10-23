using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class AssistantForm : Form
{
    internal static readonly Size DefaultLogicalSize=new Size(960,680), MinimumLogicalSize=new Size(860,600), MaximumLogicalSize=new Size(1200,860);
    internal static Size FitSize(Size logical,int dpi,Size area) { double s=Math.Max(96,dpi)/96.0; int margin=(int)Math.Round(24*s); return new Size(Math.Min((int)Math.Round(logical.Width*s),Math.Max(1,area.Width-margin)),Math.Min((int)Math.Round(logical.Height*s),Math.Max(1,area.Height-margin))); }
    readonly SensorService sensors;
    readonly Settings settings;
    readonly Panel sidebar=new Panel(),viewport=new Panel(),monitor=new Panel(),general=new Panel();
    readonly EnglishInputLock.InputPanel input=new EnglishInputLock.InputPanel();
    readonly Label brand=Ui.Label("黑皮win助手",13,true),tagline=Ui.Label("让 Windows 更顺手",9),version=Ui.Label("本机工具箱   ·   1.1.1",9),heading=Ui.Label("硬件监控",23,true),description=Ui.Label("",10);
    readonly ActionButton[] nav=new ActionButton[3];
    readonly MetricCard[] metrics={new MetricCard("CPU"),new MetricCard("GPU"),new MetricCard("内存"),new MetricCard("硬盘")};
    readonly SettingsCard widget=new SettingsCard(),position=new SettingsCard(),sensorCard=new SettingsCard(),startupCard=new SettingsCard(),behavior=new SettingsCard(),about=new SettingsCard();
    readonly ToggleSwitch widgetSwitch=new ToggleSwitch("显示任务栏监控"),startupSwitch=new ToggleSwitch("登录后自动启动");
    readonly TextBox sensorText=new TextBox();
    readonly Timer timer=new Timer();
    readonly Label runtime=Ui.Label("运行中",9);
    bool updating,ready;
    int selected;
    internal string InputStatus { get { return input.CurrentStatus; } }
    internal bool InputEnabled { get { return input.LockEnabled; } set { input.LockEnabled=value; } }
    internal event Action ExitRequested,LayoutChanged;
    internal AssistantForm(SensorService sensors,Settings settings)
    {
        this.sensors=sensors; this.settings=settings; AutoScaleMode=AutoScaleMode.None;
        Text="黑皮win助手"; Font=new Font("Microsoft YaHei UI",10F); BackColor=Ui.Background; ForeColor=Ui.Ink;
        StartPosition=FormStartPosition.CenterScreen; ClientSize=DefaultLogicalSize;
        using(var stream=typeof(Program).Assembly.GetManifestResourceStream("TaskbarMonitor.AppIcon")) Icon=new Icon(stream);
        sidebar.BackColor=Ui.Sidebar; viewport.BackColor=Ui.Background; viewport.AutoScroll=true;
        Controls.Add(sidebar); Controls.Add(viewport); Controls.Add(heading); Controls.Add(description);
        sidebar.Controls.Add(brand); sidebar.Controls.Add(tagline); sidebar.Controls.Add(version);
        tagline.ForeColor=version.ForeColor=description.ForeColor=Ui.Muted;
        string[] labels={"▦   硬件监控","A   英文输入锁","⚙   常规设置"};
        for(int i=0;i<3;i++) { int index=i; nav[i]=new ActionButton(labels[i],delegate { SelectPage(index); }) { Navigation=true }; sidebar.Controls.Add(nav[i]); }
        monitor.BackColor=general.BackColor=Ui.Background; viewport.Controls.Add(monitor); viewport.Controls.Add(input); viewport.Controls.Add(general);
        foreach(var metric in metrics) monitor.Controls.Add(metric);
        monitor.Controls.Add(widget); monitor.Controls.Add(position); monitor.Controls.Add(sensorCard); monitor.Controls.Add(runtime);
        MakeSetting(widget,"任务栏监控组件","在任务栏显示 CPU、GPU、内存和硬盘读数。",widgetSwitch);
        widgetSwitch.Checked=settings.WidgetVisible;
        widgetSwitch.CheckedChanged+=delegate { settings.WidgetVisible=widgetSwitch.Checked; SaveSettings(); if(LayoutChanged!=null) LayoutChanged(); };
        var positionActions=new Panel { BackColor=Color.White };
        var left=new ActionButton("←",delegate { settings.LeftOffset-=4; SaveSettings(); if(LayoutChanged!=null) LayoutChanged(); });
        var right=new ActionButton("→",delegate { settings.LeftOffset+=4; SaveSettings(); if(LayoutChanged!=null) LayoutChanged(); });
        var reset=new ActionButton("重置",delegate { settings.LeftOffset=0; SaveSettings(); if(LayoutChanged!=null) LayoutChanged(); });
        positionActions.Controls.Add(left); positionActions.Controls.Add(right); positionActions.Controls.Add(reset);
        positionActions.Resize+=delegate { Ui.Put(left,positionActions,0,0,34,32); Ui.Put(right,positionActions,42,0,34,32); Ui.Put(reset,positionActions,84,0,60,32); };
        MakeSetting(position,"显示位置","微调组件与天气按钮之间的距离。",positionActions,144);
        var source=Ui.Label("传感器",11,true); var refresh=new ActionButton("重新检测显卡",delegate { sensors.RefreshGpu(); });
        sensorText.Multiline=true; sensorText.ReadOnly=true; sensorText.ScrollBars=ScrollBars.Vertical; sensorText.BorderStyle=BorderStyle.None; sensorText.BackColor=Color.White; sensorText.Font=new Font("Microsoft YaHei UI",9F); sensorText.ForeColor=Ui.Muted;
        sensorCard.Controls.Add(source); sensorCard.Controls.Add(refresh); sensorCard.Controls.Add(sensorText);
        sensorCard.Resize+=delegate { double w=sensorCard.ClientSize.Width*96.0/sensorCard.DeviceDpi; Ui.Put(source,sensorCard,20,12,w-180,28); Ui.Put(refresh,sensorCard,w-148,12,128,32); Ui.Put(sensorText,sensorCard,20,56,w-40,110); };
        runtime.ForeColor=Ui.Muted;
        general.Controls.Add(startupCard); general.Controls.Add(behavior); general.Controls.Add(about);
        MakeSetting(startupCard,"登录后自动启动","进入 Windows 后，在后台启动两项功能。",startupSwitch);
        startupSwitch.Checked=StartupEnabled(); startupSwitch.CheckedChanged+=SetStartup;
        var hide=new ActionButton("收起到托盘",delegate { Hide(); });
        MakeSetting(behavior,"后台运行","关闭设置窗口后，两项功能继续工作。",hide,116);
        var aboutTitle=Ui.Label("黑皮win助手",14,true); var aboutText=Ui.Label("版本 1.1.1\n硬件监控与英文输入锁，集中在一个地方。",10); aboutText.ForeColor=Ui.Muted;
        var quit=new ActionButton("退出助手",delegate { if(ExitRequested!=null) ExitRequested(); });
        about.Controls.Add(aboutTitle); about.Controls.Add(aboutText); about.Controls.Add(quit);
        about.Resize+=delegate { double w=about.ClientSize.Width*96.0/about.DeviceDpi; Ui.Put(aboutTitle,about,20,16,w-40,28); Ui.Put(aboutText,about,20,50,w-40,58); Ui.Put(quit,about,20,126,110,32); };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
        timer.Interval=1000; timer.Tick+=delegate { if(Visible) RefreshReadings(); }; timer.Start();
        ready=true; SelectPage(0);
    }
    static void MakeSetting(SettingsCard card,string title,string text,Control action,int actionWidth=48)
    {
        var label=Ui.Label(title,11,true); var help=Ui.Label(text,9); help.ForeColor=Ui.Muted;
        card.Controls.Add(label); card.Controls.Add(help); card.Controls.Add(action);
        card.Resize+=delegate { double w=card.ClientSize.Width*96.0/card.DeviceDpi; Ui.Put(label,card,20,12,w-actionWidth-60,28); Ui.Put(help,card,20,42,w-actionWidth-60,26); Ui.Put(action,card,w-actionWidth-20,26,actionWidth,32); };
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); ApplySizeLimits(true); LayoutShell(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); ApplySizeLimits(false); LayoutShell(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); if(ready) LayoutShell(); }
    void ApplySizeLimits(bool first) {
        var work=Screen.FromControl(this).WorkingArea; MinimumSize=MaximumSize=Size.Empty;
        MaximumSize=FitSize(MaximumLogicalSize,DeviceDpi,work.Size); MinimumSize=FitSize(MinimumLogicalSize,DeviceDpi,work.Size);
        if(first) Size=FitSize(DefaultLogicalSize,DeviceDpi,work.Size);
        Location=new Point(Math.Max(work.Left,Math.Min(Left,work.Right-Width)),Math.Max(work.Top,Math.Min(Top,work.Bottom-Height)));
    }
    void SelectPage(int page) {
        selected=page;
        for(int i=0;i<nav.Length;i++) { nav[i].Selected=i==page; nav[i].Invalidate(); }
        heading.Text=new [] {"硬件监控","英文输入锁","常规设置"}[page];
        description.Text=new [] {"实时掌握电脑状态，自定义任务栏上的监控组件。","让指定游戏保持英文输入，其他软件照常使用中文。","设置启动方式，管理助手的后台运行。"}[page];
        monitor.Visible=page==0; input.Visible=page==1; general.Visible=page==2;
        viewport.AutoScrollPosition=Point.Empty; LayoutShell(); RefreshReadings();
    }
    void LayoutShell() {
        if(!ready) return;
        double w=ClientSize.Width*96.0/DeviceDpi,h=ClientSize.Height*96.0/DeviceDpi;
        Ui.Put(sidebar,this,0,0,180,h); Ui.Put(brand,sidebar,20,24,150,30); Ui.Put(tagline,sidebar,20,58,150,24);
        Ui.Put(nav[0],sidebar,10,112,160,44); Ui.Put(nav[1],sidebar,10,160,160,44); Ui.Put(nav[2],sidebar,10,Math.Max(212,h-104),160,44); Ui.Put(version,sidebar,20,h-45,150,22);
        Ui.Put(heading,this,208,22,w-236,42); Ui.Put(description,this,210,70,w-240,28); Ui.Put(viewport,this,204,116,w-216,Math.Max(80,h-132));
        double content=Math.Max(560,w-260);
        viewport.AutoScrollMinSize=new Size(Ui.Px(this,content),Ui.Px(this,selected==0?540:selected==1?460:398));
        Ui.Put(monitor,viewport,0,0,content,540); Ui.Put(input,viewport,0,0,content,460); Ui.Put(general,viewport,0,0,content,398);
        double cardWidth=(content-36)/4;
        for(int i=0;i<metrics.Length;i++) Ui.Put(metrics[i],monitor,i*(cardWidth+12),0,cardWidth,116);
        Ui.Put(widget,monitor,0,134,content,82); Ui.Put(position,monitor,0,226,content,82); Ui.Put(sensorCard,monitor,0,328,content,174); Ui.Put(runtime,monitor,0,504,content,28);
        Ui.Put(startupCard,general,0,0,content,82); Ui.Put(behavior,general,0,94,content,82); Ui.Put(about,general,0,198,content,180);
    }
    void SaveSettings() { try { settings.Save(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"设置保存失败"); } }
    static string Format(double? v,string unit) { return v.HasValue?Math.Round(v.Value).ToString("0")+unit:"—"; }
    void RefreshReadings() {
        if(!ready) return; var r=sensors.Snapshot();
        metrics[0].Value=Format(r.Cpu,"%"); metrics[0].Detail="温度  "+Format(r.CpuTemp,"°C"); metrics[1].Value=Format(r.GpuLoad,"%"); metrics[1].Detail="温度  "+Format(r.GpuTemp,"°C"); metrics[2].Value=Format(r.Memory,"%"); metrics[2].Detail="物理内存占用"; metrics[3].Value=Format(r.DiskTemp,"°C"); metrics[3].Detail="最高主温度";
        foreach(var metric in metrics) metric.Invalidate();
        sensorText.Text=r.CpuSensor+Environment.NewLine+r.DiskSensor+Environment.NewLine+string.Join(Environment.NewLine,r.Gpus.Select(g=>g.Name+"   "+Format(g.Load,"%")+" / "+Format(g.Temperature,"°C")))+(string.IsNullOrEmpty(r.Error)?"":Environment.NewLine+r.Error);
        using(var p=Process.GetCurrentProcess()) runtime.Text="正在运行  ·  内存 "+(p.WorkingSet64/1048576.0).ToString("F0")+" MB";
    }
    internal void ShowMain(int page) { updating=true; startupSwitch.Checked=StartupEnabled(); widgetSwitch.Checked=settings.WidgetVisible; updating=false; if(page>=0) SelectPage(page); Show(); WindowState=FormWindowState.Normal; BringToFront(); Activate(); }
    static bool StartupEnabled() {
        try { var r=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.AppDirectory,"startup-registration.json"))); return File.Exists((string)r["ShortcutPath"]); } catch { return false; }
    }
    void SetStartup(object sender,EventArgs e) {
        if(updating) return;
        try { using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Program.AppDirectory,"TaskbarMonitorStartup.exe"),startupSwitch.Checked?"--install-startup":"--remove-startup") { UseShellExecute=false,CreateNoWindow=true })) { if(!p.WaitForExit(10000)) throw new IOException("配置仍在处理中，请稍后重新打开设置核对。"); if(p.ExitCode!=0) throw new IOException("配置失败，请查看 startup-launcher.log。"); } }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"自启动设置"); }
        finally { updating=true; startupSwitch.Checked=StartupEnabled(); updating=false; }
    }
    protected override void Dispose(bool disposing) { if(disposing) timer.Dispose(); base.Dispose(disposing); }
}
