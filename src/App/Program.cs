using ClipManager;

namespace NvidiaClipManager;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var preview=args.Contains("--preview");
        using var mutex=new Mutex(true,preview?"Local\\NvidiaClipManagerPreview":"Local\\NvidiaClipManager",out var first);
        if(!first) { MessageBox.Show("NVIDIA Clip Manager is already running. Open its green tray icon near the clock."); return; }
        var data=preview?Path.Combine(Path.GetTempPath(),"NcmPreview"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NvidiaClipManager");
        try { Application.Run(new MainForm(new Store(data),preview,args)); }
        catch(Exception ex) { MessageBox.Show(ex.Message,"NVIDIA Clip Manager",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
}
