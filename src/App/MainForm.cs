using System.Diagnostics;
using ClipManager;
using Microsoft.Win32;

namespace NvidiaClipManager;

public sealed class MainForm : Form
{
    private readonly Store store;
    private Settings settings;
    private readonly bool preview;
    private readonly TextBox clips=new(),backups=new(),cloud=new();
    private readonly CheckBox cloudOn=new(){Text="Automatically copy finished clips to iCloud Photos"},startup=new(){Text="Start with Windows"};
    private readonly Label status=new(),description=new();
    private readonly Button start=new(){Text="Start watching"},existing=new(){Text="Process existing clips"},stop=new(){Text="Pause"};
    private readonly DataGridView grid=new();
    private readonly NotifyIcon tray=new();
    private readonly Icon icon;
    private CancellationTokenSource? cancellation;
    private Task? work;
    private bool exiting;
    private static readonly Color Background=Color.FromArgb(20,23,28),Card=Color.FromArgb(30,35,42),TextColor=Color.FromArgb(234,239,244),Muted=Color.FromArgb(165,178,189),Green=Color.FromArgb(154,224,76);

    public MainForm(Store store,bool preview,string[] args)
    {
        this.store=store; this.preview=preview; settings=store.LoadSettings();
        if(preview) settings=new Settings{ClipsFolder=@"C:\Users\You\Videos\NVIDIA",BackupFolder=@"C:\Users\You\Videos\NVIDIA Clip Backups",ICloudFolder=@"C:\Users\You\Pictures\iCloud Photos\Photos",ICloudEnabled=true};
        Text="NVIDIA Clip Manager"+(preview?" • Preview":""); ClientSize=new(1130,780); MinimumSize=new(960,740); StartPosition=FormStartPosition.CenterScreen;
        BackColor=Background; ForeColor=TextColor; Font=new("Segoe UI",10); AutoScaleMode=AutoScaleMode.Dpi;
        using(var bitmap=new Bitmap(32,32)) { using(var g=Graphics.FromImage(bitmap)) { g.Clear(Green); g.FillRectangle(Brushes.Black,7,8,18,16); using var brush=new SolidBrush(Green); g.FillPolygon(brush,new Point[]{new(13,11),new(13,21),new(21,16)}); } var handle=bitmap.GetHicon(); icon=(Icon)Icon.FromHandle(handle).Clone(); DestroyIcon(handle); }
        Icon=icon;
        var title=new Label{Text="NVIDIA Clip Manager",Font=new("Segoe UI",25,FontStyle.Bold),Location=new(26,20),Size=new(900,45)}; Controls.Add(title);
        Controls.Add(new Label{Text="Clean clips. Safe originals. Ready to share.",ForeColor=Muted,Location=new(28,72),Size=new(950,25)});
        var config=new Panel{Location=new(26,113),Size=new(1078,248),BackColor=Card,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right}; Controls.Add(config);
        AddFolder(config,"NVIDIA clips",clips,settings.ClipsFolder,18);
        AddFolder(config,"Original backups",backups,settings.BackupFolder,64);
        AddFolder(config,"iCloud Photos",cloud,settings.ICloudFolder,110);
        cloudOn.SetBounds(172,157,710,30); cloudOn.Checked=settings.ICloudEnabled; config.Controls.Add(cloudOn);
        config.Controls.Add(new Label{Text="Optional upload to your Apple account. Requires iCloud for Windows with Photos enabled.",ForeColor=Muted,Location=new(173,188),Size=new(850,25)});
        startup.SetBounds(172,213,300,27); startup.Checked=settings.StartWithWindows; config.Controls.Add(startup);
        cloudOn.CheckedChanged+=(_,_)=>cloud.Enabled=cloudOn.Checked; cloud.Enabled=cloudOn.Checked;
        start.SetBounds(26,379,185,38); existing.SetBounds(222,379,202,38); stop.SetBounds(435,379,90,38);
        foreach(var b in new[]{start,existing,stop}) { Style(b); Controls.Add(b); }
        start.BackColor=Green; start.ForeColor=Color.Black; stop.Enabled=false;
        status.SetBounds(545,379,560,26); status.Font=new("Segoe UI",12,FontStyle.Bold); status.Text="Ready"; status.ForeColor=Green; Controls.Add(status);
        description.SetBounds(545,405,560,40); description.Text="Waiting for you to choose a folder and start."; description.ForeColor=Muted; Controls.Add(description);
        grid.SetBounds(26,454,1078,238); grid.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        grid.ReadOnly=true; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect=false; grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; grid.BackgroundColor=Card; grid.BorderStyle=BorderStyle.None; grid.EnableHeadersVisualStyles=false;
        grid.ColumnHeadersDefaultCellStyle=new(){BackColor=Card,ForeColor=Muted,Font=new("Segoe UI",10,FontStyle.Bold)};
        grid.DefaultCellStyle=new(){BackColor=Card,ForeColor=TextColor,SelectionBackColor=Color.FromArgb(53,70,44),SelectionForeColor=TextColor};
        grid.GridColor=Background; grid.RowTemplate.Height=35;
        grid.Columns.Add("clip","CLIP"); grid.Columns.Add("state","STATUS"); grid.Columns.Add("cloud","ICLOUD"); grid.Columns[0].FillWeight=50; grid.Columns[1].FillWeight=23; grid.Columns[2].FillWeight=27;
        Controls.Add(grid);
        var open=new Button{Text="Open clip folder",Location=new(26,709),Size=new(150,34),Anchor=AnchorStyles.Bottom|AnchorStyles.Left};
        var restore=new Button{Text="Restore original",Location=new(186,709),Size=new(145,34),Anchor=open.Anchor};
        var retry=new Button{Text="Retry selected",Location=new(341,709),Size=new(140,34),Anchor=open.Anchor};
        var details=new Button{Text="Details",Location=new(491,709),Size=new(100,34),Anchor=open.Anchor};
        foreach(var b in new[]{open,restore,retry,details}) { Style(b); Controls.Add(b); }
        Controls.Add(new Label{Text="Independent open-source app • v0.1.0 • No NVIDIA affiliation",ForeColor=Muted,Location=new(26,752),Size=new(1020,22),Anchor=AnchorStyles.Bottom|AnchorStyles.Left});
        start.Click+=async(_,_)=>await Begin(false);
        existing.Click+=async(_,_)=>await Begin(true);
        stop.Click+=(_,_)=>Pause();
        open.Click+=(_,_)=> { if(Directory.Exists(clips.Text)) Process.Start(new ProcessStartInfo("explorer.exe"){ArgumentList={clips.Text},UseShellExecute=true}); };
        details.Click+=(_,_)=> { if(Selected() is {} j) MessageBox.Show($"{j.Source}\n\n{j.State}\n{j.Detail}\n\niCloud: {j.CloudState}\n\nRecorded: {j.RecordedAt}\nBackup: {j.Backup}","Clip details"); };
        restore.Click+=(_,_)=> { if(work is {IsCompleted:false}) { MessageBox.Show("Pause and wait for processing to stop before restoring."); return; } if(Selected() is {} j) { try { new Engine(store,new Media(ToolsFolder())).Restore(j); RefreshJobs(); } catch(Exception e){MessageBox.Show(e.Message);} } };
        retry.Click+=async(_,_)=> { if(Selected() is {} j && work is not {IsCompleted:false}) await Retry(j); };
        var menu=new ContextMenuStrip(); menu.Items.Add("Open",null,(_,_)=>ShowMain()); menu.Items.Add("Pause",null,(_,_)=>Pause()); menu.Items.Add("Quit",null,async(_,_)=>await Quit());
        tray.Icon=icon; tray.Text="NVIDIA Clip Manager"; tray.ContextMenuStrip=menu; tray.Visible=!preview; tray.DoubleClick+=(_,_)=>ShowMain();
        FormClosing+=(_,e)=>{ if(!exiting && !preview){e.Cancel=true; Hide(); tray.ShowBalloonTip(2500,"Still running", "Clip Manager is in the tray near the clock. Choose Quit to exit.",ToolTipIcon.Info);} };
        Shown+=async(_,_)=> {
            RefreshJobs();
            if(preview) {
                grid.Rows.Add("Desktop 2026.09.04 - 22.09.52.02 repaired.mp4","Repaired · verified","Queued to iCloud");
                grid.Rows.Add("Minecraft 2026.09.04 - 21.45.08.01.mp4","Normalized · lossless","Not requested");
                grid.Rows.Add("Desktop 2026.09.04 - 20.30.12.00.DVR.mp4","Needs attention","Original kept");
                status.Text="Watching"; description.Text="Finished clips are checked automatically. Preview only.";
                if(args.Contains("--screenshot")) { var at=Array.IndexOf(args,"--screenshot"); using var bmp=new Bitmap(Width,Height); DrawToBitmap(bmp,new Rectangle(0,0,Width,Height)); bmp.Save(args[at+1]); exiting=true; Close(); }
            } else if(settings.Watching) { if(args.Contains("--tray")) Hide(); await Begin(false); }
        };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    private static void Style(Button b){ b.FlatStyle=FlatStyle.Flat; b.FlatAppearance.BorderColor=Color.FromArgb(70,80,90); b.BackColor=Card; b.ForeColor=TextColor; b.Cursor=Cursors.Hand; }
    private void AddFolder(Panel parent,string label,TextBox box,string value,int y)
    {
        parent.Controls.Add(new Label{Text=label,Location=new(18,y+8),Size=new(150,25),ForeColor=Muted});
        box.SetBounds(172,y+3,770,29); box.Text=value; box.BackColor=Background; box.ForeColor=TextColor; box.BorderStyle=BorderStyle.FixedSingle; box.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; parent.Controls.Add(box);
        var choose=new Button{Text="Browse",Location=new(953,y),Size=new(105,34),Anchor=AnchorStyles.Top|AnchorStyles.Right}; Style(choose); parent.Controls.Add(choose);
        choose.Click+=(_,_)=>{ using var dialog=new FolderBrowserDialog{SelectedPath=box.Text}; if(dialog.ShowDialog(this)==DialogResult.OK) box.Text=dialog.SelectedPath; };
    }
    private string ToolsFolder()
    {
        var local=Path.Combine(store.Root,"tools");
        if(File.Exists(Path.Combine(local,"ffmpeg.exe"))) return local;
        return Path.Combine(AppContext.BaseDirectory,"tools");
    }
    private void SaveSettings()
    {
        settings.ClipsFolder=Path.GetFullPath(clips.Text.Trim()); settings.BackupFolder=Path.GetFullPath(backups.Text.Trim());
        settings.ICloudFolder=cloud.Text.Trim(); settings.ICloudEnabled=cloudOn.Checked; settings.StartWithWindows=startup.Checked;
        settings.Validate(); store.SaveSettings(settings);
        using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(settings.StartWithWindows) run.SetValue("NvidiaClipManager","\""+Environment.ProcessPath+"\" --tray"); else run.DeleteValue("NvidiaClipManager",false);
    }
    private async Task Begin(bool includeExisting)
    {
        if(preview || work is {IsCompleted:false}) return;
        try {
            SaveSettings();
            if(!File.Exists(Path.Combine(ToolsFolder(),"ffprobe.exe"))) throw new IOException("Recovery engine is not installed. Run the NVIDIA Clip Manager installer.");
            settings.Watching=true; store.SaveSettings(settings); cancellation=new();
            var engine=new Engine(store,new Media(ToolsFolder())); engine.Changed+=OnChanged;
            var watcher=new Watcher(store,engine); watcher.Status+=text=>Ui(()=>status.Text=text);
            SetBusy(true); status.Text="Watching"; description.Text=includeExisting?"Existing and new clips will be checked one at a time.":"New clips will be checked once NVIDIA finishes writing.";
            work=Task.Run(()=>watcher.Run(settings,includeExisting,cancellation.Token));
            await work;
        } catch(OperationCanceledException) {status.Text="Paused";}
        catch(Exception ex){status.Text="Needs attention"; MessageBox.Show(ex.Message,"NVIDIA Clip Manager");}
        finally {SetBusy(false);}
    }
    private async Task Retry(Job job)
    {
        if(preview) return;
        try { SaveSettings(); cancellation=new(); var engine=new Engine(store,new Media(ToolsFolder())); engine.Changed+=OnChanged; SetBusy(true);
            work=Task.Run(async()=>{ if(job.State is "Completed" or "Unchanged" && job.CloudRequested) await engine.ExportCloud(job,settings,cancellation.Token); else await engine.Process(job.Source,settings,cancellation.Token); }); await work;
        } catch(Exception ex){MessageBox.Show(ex.Message);} finally{SetBusy(false); RefreshJobs();}
    }
    private void SetBusy(bool busy){start.Enabled=existing.Enabled=!busy; stop.Enabled=busy; clips.Enabled=backups.Enabled=cloudOn.Enabled=startup.Enabled=!busy; cloud.Enabled=!busy&&cloudOn.Checked;}
    private void Pause(){ settings.Watching=false; store.SaveSettings(settings); cancellation?.Cancel(); status.Text="Pausing…"; }
    private async Task Quit(){ Pause(); try{if(work!=null)await work;}catch{} exiting=true; tray.Visible=false; Close(); }
    private void ShowMain(){Show(); WindowState=FormWindowState.Normal; Activate();}
    private Job? Selected()=>grid.SelectedRows.Count>0?grid.SelectedRows[0].Tag as Job:null;
    private void Ui(Action action){if(!IsDisposed && IsHandleCreated) BeginInvoke(action);}
    private void OnChanged(Job job)=>Ui(()=>{status.Text=job.State; description.Text=Path.GetFileName(job.Source); RefreshJobs();});
    private void RefreshJobs(){grid.Rows.Clear();foreach(var j in store.Jobs().Take(250)){var row=grid.Rows.Add(Path.GetFileName(string.IsNullOrEmpty(j.Output)?j.Source:j.Output),j.State,j.CloudState); grid.Rows[row].Tag=j; grid.Rows[row].Cells[1].ToolTipText=j.Detail;}}
    protected override void Dispose(bool disposing){if(disposing){tray.Dispose();icon.Dispose();cancellation?.Dispose();}base.Dispose(disposing);}
}
