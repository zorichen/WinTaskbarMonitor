using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class Ui
{
    internal static readonly Color Background=Color.FromArgb(247,248,250), Sidebar=Color.FromArgb(239,242,246), Ink=Color.FromArgb(28,32,38), Muted=Color.FromArgb(100,108,120), Accent=Color.FromArgb(0,103,192), Border=Color.FromArgb(225,229,235);
    internal static int Px(Control c,double value) { return (int)Math.Round(value*c.DeviceDpi/96.0); }
    internal static void Put(Control c,Control parent,double x,double y,double width,double height) { c.SetBounds(Px(parent,x),Px(parent,y),Px(parent,width),Px(parent,height)); }
    internal static Label Label(string text,float size,bool bold=false) { return new Label { Text=text,Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,BackColor=Color.Transparent,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft }; }
    internal static GraphicsPath Round(RectangleF r,float radius) {
        var p=new GraphicsPath(); float d=radius*2;
        p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
}
internal sealed class AssistantMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item=e.Item as ToolStripMenuItem;
        if(item==null) { base.OnRenderItemText(e); return; }
        int left=item.Padding.Left,right=item.Padding.Right;
        var color=item.Enabled?Ui.Ink:Ui.Muted;
        var bounds=new Rectangle(left,0,Math.Max(1,item.Width-left-right),item.Height);
        TextRenderer.DrawText(e.Graphics,item.Text,item.Font,bounds,color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        if(item.Checked) TextRenderer.DrawText(e.Graphics,"✓",item.Font,new Rectangle(0,0,left,item.Height),Ui.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
}
internal sealed class SettingsCard : Panel
{
    internal SettingsCard() { DoubleBuffered=true; BackColor=Ui.Background; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Ui.Round(new RectangleF(0.5f,0.5f,Width-1,Height-1),Ui.Px(this,8)))
        using(var fill=new SolidBrush(Color.White)) using(var pen=new Pen(Ui.Border)) { e.Graphics.FillPath(fill,path); e.Graphics.DrawPath(pen,path); }
    }
}
internal sealed class ActionButton : Button
{
    internal bool Primary,Navigation,Selected;
    bool hover;
    internal ActionButton(string text,EventHandler click) {
        Text=text; Font=new Font("Microsoft YaHei UI",10F); FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0;
        Cursor=Cursors.Hand; UseVisualStyleBackColor=false; SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        Click+=click; MouseEnter+=delegate { hover=true; Invalidate(); }; MouseLeave+=delegate { hover=false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent==null?Ui.Background:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color color=Primary?Ui.Accent:Navigation?(Selected?Color.FromArgb(220,233,247):hover?Color.FromArgb(228,233,240):Ui.Sidebar):hover?Color.FromArgb(245,247,250):Color.White;
        using(var path=Ui.Round(new RectangleF(0.5f,0.5f,Width-1,Height-1),Ui.Px(this,6)))
        using(var fill=new SolidBrush(color)) using(var border=new Pen(Primary?Ui.Accent:Navigation?color:Ui.Border)) { e.Graphics.FillPath(fill,path); e.Graphics.DrawPath(border,path); }
        if(Navigation && Selected) using(var b=new SolidBrush(Ui.Accent)) e.Graphics.FillRectangle(b,Ui.Px(this,3),Ui.Px(this,12),Ui.Px(this,3),Height-Ui.Px(this,24));
        Rectangle textBounds=new Rectangle(Ui.Px(this,Navigation?18:8),0,Width-Ui.Px(this,Navigation?28:16),Height);
        TextRenderer.DrawText(e.Graphics,Text,Font,textBounds,Enabled?(Primary?Color.White:Ui.Ink):Ui.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|(Navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-Ui.Px(this,4),-Ui.Px(this,4)));
    }
}
internal sealed class ToggleSwitch : CheckBox
{
    internal ToggleSwitch(string name) { AccessibleName=name; Text=""; AutoSize=false; Cursor=Cursors.Hand; SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); CheckedChanged+=delegate { Invalidate(); }; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Color.White); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float h=Ui.Px(this,22),w=Ui.Px(this,42),top=(Height-h)/2;
        using(var path=Ui.Round(new RectangleF(1,top,w,h),h/2)) using(var b=new SolidBrush(Checked?Ui.Accent:Color.FromArgb(143,150,160))) e.Graphics.FillPath(b,path);
        float d=h-Ui.Px(this,6),x=Checked?w-d-Ui.Px(this,2):Ui.Px(this,4);
        using(var b=new SolidBrush(Color.White)) e.Graphics.FillEllipse(b,x,top+Ui.Px(this,3),d,d);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle);
    }
}
internal sealed class MetricCard : SettingsCardBase
{
    internal string Caption,Value="—",Detail="等待传感器";
    internal MetricCard(string caption) { Caption=caption; }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        using(var name=new Font("Microsoft YaHei UI",10F)) using(var value=new Font("Segoe UI",25F,FontStyle.Bold)) using(var detail=new Font("Microsoft YaHei UI",9F)) {
            TextRenderer.DrawText(e.Graphics,Caption,name,new Rectangle(Ui.Px(this,16),Ui.Px(this,12),Width-Ui.Px(this,32),Ui.Px(this,22)),Ui.Muted,TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics,Value,value,new Rectangle(Ui.Px(this,14),Ui.Px(this,34),Width-Ui.Px(this,28),Ui.Px(this,44)),Ui.Ink,TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics,Detail,detail,new Rectangle(Ui.Px(this,16),Ui.Px(this,82),Width-Ui.Px(this,32),Ui.Px(this,22)),Ui.Muted,TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        }
    }
}
internal class SettingsCardBase : Panel
{
    internal SettingsCardBase() { DoubleBuffered=true; BackColor=Ui.Background; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Ui.Round(new RectangleF(.5f,.5f,Width-1,Height-1),Ui.Px(this,8))) using(var brush=new SolidBrush(Color.White)) using(var pen=new Pen(Ui.Border)) { e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(pen,path); }
    }
}
