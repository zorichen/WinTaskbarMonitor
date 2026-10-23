using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
namespace EnglishInputLock
{
    internal sealed class InputPanel : UserControl
    {
        readonly Settings settings;
        readonly Engine engine;
        readonly ListView list=new ListView();
        readonly Label status=Ui.Label("",10),heading=Ui.Label("启用英文输入锁",11,true),hint=Ui.Label("进入指定程序时自动切换为美式英文键盘。",9),rulesTitle=Ui.Label("程序规则",11,true),note=Ui.Label("只对勾选的程序生效，设置会自动保存。",9);
        readonly ToggleSwitch enabled=new ToggleSwitch("启用英文输入锁");
        readonly SettingsCard toggleCard=new SettingsCard(),rulesCard=new SettingsCard();
        readonly ActionButton addFile,addRunning,remove;
        readonly Timer timer=new Timer();
        bool updating,readOnly,ready;
        string lastStatus="";
        public string CurrentStatus { get { return engine.State; } }
        internal bool LockEnabled { get { return settings.Enabled; } set { enabled.Checked=value; } }
        public InputPanel()
        {
            AutoScaleMode=AutoScaleMode.None; BackColor=Ui.Background; Font=new Font("Microsoft YaHei UI",10F);
            try { settings=File.Exists(Store.FileName)?Store.Read(Store.FileName):new Settings(); if(!File.Exists(Store.FileName)) Store.Save(Store.FileName,settings); }
            catch(Exception) { settings=new Settings { Enabled=false }; readOnly=true; }
            var clock=Stopwatch.StartNew(); engine=new Engine(new Desktop(),()=>clock.ElapsedMilliseconds);
            Controls.Add(toggleCard); Controls.Add(rulesCard); Controls.Add(status); Controls.Add(note);
            toggleCard.Controls.Add(heading); toggleCard.Controls.Add(hint); toggleCard.Controls.Add(enabled); hint.ForeColor=note.ForeColor=Ui.Muted;
            enabled.Checked=settings.Enabled;
            addFile=new ActionButton("＋ 添加程序",delegate { AddFile(); }) { Primary=true };
            addRunning=new ActionButton("从运行中选择",delegate { AddRunning(); });
            remove=new ActionButton("移除",delegate { RemoveSelected(); });
            rulesCard.Controls.Add(rulesTitle); rulesCard.Controls.Add(addFile); rulesCard.Controls.Add(addRunning); rulesCard.Controls.Add(remove); rulesCard.Controls.Add(list);
            list.View=View.Details; list.CheckBoxes=true; list.FullRowSelect=true; list.HideSelection=false; list.BorderStyle=BorderStyle.None; list.BackColor=Color.White;
            list.Columns.Add("程序",180); list.Columns.Add("位置",420);
            enabled.CheckedChanged+=delegate { if(readOnly) { enabled.Checked=false; return; } settings.Enabled=enabled.Checked; Save(); };
            list.ItemChecked+=delegate(object sender,ItemCheckedEventArgs e) { if(updating || readOnly) return; ((Rule)e.Item.Tag).Enabled=e.Item.Checked; Save(); };
            RefreshRules(); timer.Interval=100; timer.Tick+=delegate { RefreshStatus(); }; timer.Start();
            ready=true; LayoutPage();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if(ready) LayoutPage(); }
        void LayoutPage()
        {
            double w=ClientSize.Width*96.0/DeviceDpi;
            Ui.Put(toggleCard,this,0,0,w,82); Ui.Put(heading,toggleCard,20,12,w-100,28); Ui.Put(hint,toggleCard,20,42,w-100,26); Ui.Put(enabled,toggleCard,w-68,26,48,32);
            Ui.Put(rulesCard,this,0,100,w,260); Ui.Put(rulesTitle,rulesCard,20,18,100,32);
            Ui.Put(addFile,rulesCard,w-358,18,112,32); Ui.Put(addRunning,rulesCard,w-236,18,128,32); Ui.Put(remove,rulesCard,w-98,18,78,32);
            Ui.Put(list,rulesCard,20,68,w-40,174); list.Columns[0].Width=Ui.Px(this,170); list.Columns[1].Width=Ui.Px(this,Math.Max(340,w-235));
            Ui.Put(note,this,4,370,w-8,26); Ui.Put(status,this,4,408,w-8,48);
        }
        void Save()
        {
            if (readOnly) return;
            try { Store.Save(Store.FileName, settings); }
            catch (Exception ex) { settings.Enabled = false; readOnly = true; enabled.Checked = false; MessageBox.Show(this, "保存失败，已暂停。\n" + ex.Message, "保存配置"); }
        }
        void RefreshRules()
        {
            updating = true; list.Items.Clear();
            foreach (Rule rule in settings.Rules)
            {
                var item = new ListViewItem(System.IO.Path.GetFileName(rule.Path)) { Tag = rule, Checked = rule.Enabled };
                item.SubItems.Add(rule.Path); list.Items.Add(item);
            }
            updating = false;
        }
        void Add(string path)
        {
            if (readOnly) return;
            path = System.IO.Path.GetFullPath(path);
            if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            var found = settings.Rules.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            if (found != null) found.Enabled = true;
            else settings.Rules.Add(new Rule { Path = path });
            Save(); RefreshRules();
        }
        void AddFile()
        {
            using (var picker = new OpenFileDialog { Title = "选择需要锁定英文的程序", Filter = "应用程序 (*.exe)|*.exe", CheckFileExists = true })
                if (picker.ShowDialog(FindForm()) == DialogResult.OK) Add(picker.FileName);
        }
        void RemoveSelected()
        {
            if (readOnly) return;
            foreach (ListViewItem item in list.SelectedItems) settings.Rules.Remove((Rule)item.Tag);
            Save(); RefreshRules();
        }
        void AddRunning()
        {
            using (var picker = new Form { Text = "选择正在运行的程序", Size = new Size(760, 400), StartPosition = FormStartPosition.CenterParent, Font = Font })
            {
                var items = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
                items.Columns.Add("程序", 185); items.Columns.Add("位置", 535);
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var process in Process.GetProcesses())
                    using (process) try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero || process.Id == Process.GetCurrentProcess().Id) continue;
                        string path = Desktop.ProcessPath((uint)process.Id);
                        if (path.Length == 0 || !paths.Add(path)) continue;
                        var item = new ListViewItem(System.IO.Path.GetFileName(path)) { Tag = path }; item.SubItems.Add(path); items.Items.Add(item);
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                var add = new Button { Dock = DockStyle.Bottom, Height = 40, Text = "添加选中程序" };
                EventHandler accept = delegate { if (items.SelectedItems.Count > 0) { Add((string)items.SelectedItems[0].Tag); picker.Close(); } };
                add.Click += accept; items.DoubleClick += accept; picker.Controls.Add(items); picker.Controls.Add(add); picker.ShowDialog(FindForm());
            }
        }

        void RefreshStatus()
        {
            try { engine.Tick(settings); } catch(Exception ex) { engine.State="检查失败 · "+ex.GetType().Name; }
            string text=readOnly ? "配置读取或保存失败 · 已暂停，原文件保留："+Store.FileName : engine.State;
            status.Text="●  "+text; status.ForeColor=readOnly?Color.FromArgb(173,74,25):Color.FromArgb(37,110,82);
            if(text==lastStatus) return;
            lastStatus=text;
            try { File.WriteAllText(Path.Combine(Store.Folder,"status.txt"),DateTimeOffset.Now.ToString("o")+"\n"+text+"\n"+engine.LayoutHex+"\nrequests="+engine.Requests,Encoding.UTF8); }
            catch(IOException) {} catch(UnauthorizedAccessException) {}
        }
        protected override void Dispose(bool disposing) { if(disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
