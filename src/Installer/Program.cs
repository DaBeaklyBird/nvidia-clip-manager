using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace NvidiaClipManagerSetup;

internal static class Program
{
    public const string Version="0.1.0";
    public const string EngineUrl="https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-essentials_build.zip";
    public const string EngineSha="6f58ce889f59c311410f7d2b18895b33c03456463486f3b1ebc93d97a0f54541";
    public static readonly string AppRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","NvidiaClipManager");
    public static readonly string DataRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NvidiaClipManager");
    [STAThread] private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Length==2 && args[0]=="--test-extract") { Extract(args[1]); return; }
        if(args.Contains("--uninstall")) { Uninstall(); return; }
        Application.Run(new SetupForm());
    }
    public static void Extract(string destination)
    {
        Directory.CreateDirectory(destination);
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip") ?? throw new IOException("Installer payload missing.");
        using var zip=new ZipArchive(resource);
        foreach(var entry in zip.Entries) {
            var path=Path.GetFullPath(Path.Combine(destination,entry.FullName));
            if(!path.StartsWith(Path.GetFullPath(destination).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe installer entry.");
            if(string.IsNullOrEmpty(entry.Name))continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path,false);
        }
    }
    public static async Task<string> Install(Action<string> status)
    {
        // A running app must finish its current replacement transaction before an upgrade.
        using(var mutex=new Mutex(true,"Local\\NvidiaClipManager",out var acquired)) {
            if(!acquired) throw new IOException("Quit NVIDIA Clip Manager from its tray menu, then run setup again.");
        }
        var versionFolder=Path.Combine(AppRoot,Version);
        if(Directory.Exists(versionFolder)) throw new IOException("Version "+Version+" is already installed. Open it from Start, or uninstall it before reinstalling.");
        var staging=Path.Combine(AppRoot,"install-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        status("Unpacking app…"); await Task.Run(()=>Extract(staging));
        Directory.CreateDirectory(DataRoot);
        var download=Path.Combine(DataRoot,"engine-download.zip");
        var engineFolder=Path.Combine(DataRoot,"engine-"+Guid.NewGuid().ToString("N"));
        status("Downloading FFmpeg 8.1.1 from its publisher (about 100 MB)…");
        using(var client=new HttpClient{Timeout=TimeSpan.FromMinutes(20)}) {
            using var response=await client.GetAsync(EngineUrl,HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
            using var file=new FileStream(download,FileMode.Create,FileAccess.Write,FileShare.None); await response.Content.CopyToAsync(file);
        }
        status("Verifying engine checksum…");
        using(var file=File.OpenRead(download)) {if(!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(EngineSha,StringComparison.OrdinalIgnoreCase))throw new IOException("Download checksum mismatch. App not installed.");}
        await Task.Run(()=>ZipFile.ExtractToDirectory(download,engineFolder));
        var ff=Directory.GetFiles(engineFolder,"ffmpeg.exe",SearchOption.AllDirectories).Single(); var bin=Path.GetDirectoryName(ff)!;
        var tools=Path.Combine(DataRoot,"tools"); Directory.CreateDirectory(tools);
        foreach(var name in new[]{"ffmpeg.exe","ffprobe.exe"})File.Copy(Path.Combine(bin,name),Path.Combine(tools,name),true);
        // Keep all the engine's documentation/license files in its extracted folder.
        status("Registering app and shortcuts…");
        Directory.Move(staging,versionFolder);
        var exe=Path.Combine(versionFolder,"NvidiaClipManager.exe");
        var uninstall=Path.Combine(AppRoot,"Uninstall.exe"); File.Copy(Environment.ProcessPath!,uninstall,true);
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NvidiaClipManager")) {
            key.SetValue("DisplayName","NVIDIA Clip Manager");key.SetValue("DisplayVersion",Version);key.SetValue("Publisher","DaBeaklyBird (independent project)");key.SetValue("InstallLocation",versionFolder);key.SetValue("DisplayIcon",exe);key.SetValue("UninstallString","\""+uninstall+"\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
        }
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut=shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"NVIDIA Clip Manager.lnk")); shortcut.TargetPath=exe;shortcut.WorkingDirectory=versionFolder;shortcut.Save();
        return exe;
    }
    private static void Uninstall()
    {
        if(MessageBox.Show("Uninstall NVIDIA Clip Manager? Your clips, original backups, settings and repair history will stay intact.","NVIDIA Clip Manager",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        try {
            using var mutex=new Mutex(true,"Local\\NvidiaClipManager",out var acquired);
            if(!acquired)throw new IOException("Quit Clip Manager from its tray menu before uninstalling.");
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NvidiaClipManager")) {
                var path=key?.GetValue("InstallLocation") as string;
                if(path!=null && Path.GetFullPath(path).StartsWith(AppRoot+"\\",StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path)==Version && (File.GetAttributes(path)&FileAttributes.ReparsePoint)==0) Directory.Delete(path,true);
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NvidiaClipManager",false);
            using(var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)) run?.DeleteValue("NvidiaClipManager",false);
            File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"NVIDIA Clip Manager.lnk"));
            MessageBox.Show("App removed. Clips, backups and history were preserved. The inactive uninstaller and recovery engine remain in LocalAppData.");
        }catch(Exception ex){MessageBox.Show(ex.Message);}
    }
}

internal sealed class SetupForm:Form
{
    public SetupForm()
    {
        Text="Install NVIDIA Clip Manager";ClientSize=new(590,380);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;Font=new("Segoe UI",10);
        Controls.Add(new Label{Text="NVIDIA Clip Manager",Font=new("Segoe UI",22,FontStyle.Bold),Location=new(24,20),Size=new(540,50)});
        Controls.Add(new Label{Text="Automatic clip checks, verified repairs and optional iCloud Photos export.\n\nInstalls for your Windows account. Original recordings are backed up before replacement. iCloud upload is off until you enable it.\n\nSetup downloads the FFmpeg engine from GyanD on GitHub and checks its SHA-256 hash. Internet access is required.",Location=new(26,83),Size=new(535,145)});
        var terms=new LinkLabel{Text="App license (MIT) and FFmpeg license (GPL)",Location=new(26,231),Size=new(535,24)};Controls.Add(terms);
        terms.LinkClicked+=(_,_)=>Process.Start(new ProcessStartInfo("https://github.com/DaBeaklyBird/nvidia-clip-manager/blob/main/THIRD-PARTY.md"){UseShellExecute=true});
        var agree=new CheckBox{Text="I have reviewed and accept these software licenses.",Location=new(26,260),Size=new(535,28)};Controls.Add(agree);
        var status=new Label{Text="v0.1.0 • Independent project, not affiliated with NVIDIA or Apple.",Location=new(26,304),Size=new(535,48)};Controls.Add(status);
        var button=new Button{Text="Install",Location=new(435,340),Size=new(120,30),Enabled=false};Controls.Add(button);agree.CheckedChanged+=(_,_)=>button.Enabled=agree.Checked;
        string? installedExe=null;
        button.Click+=async(_,_)=>{
            if(installedExe!=null){Process.Start(new ProcessStartInfo(installedExe){UseShellExecute=true});Close();return;}
            button.Enabled=agree.Enabled=false;
            try {installedExe=await Program.Install(s=>status.Text=s); status.Text="Installed. Open the app to choose folders and start watching.";button.Text="Open app";button.Enabled=true;}
            catch(Exception ex){MessageBox.Show(ex.Message,"Setup could not finish");button.Enabled=agree.Enabled=true;}
        };
    }
}
