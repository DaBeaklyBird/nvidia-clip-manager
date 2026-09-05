using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipManager;

public sealed record RunResult(int Code,string Output,string Errors);
public static class Runner
{
    public static async Task<RunResult> Run(string exe,IEnumerable<string> arguments,CancellationToken token,TimeSpan? timeout=null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout ?? TimeSpan.FromHours(12));
        using var p = new Process { StartInfo = new(exe) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true } };
        foreach(var a in arguments) p.StartInfo.ArgumentList.Add(a);
        p.Start();
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* Optional on restricted systems. */ }
        async Task<string> Drain(StreamReader reader) {
            var text = new StringBuilder(); var buffer = new char[8192]; int n;
            while((n=await reader.ReadAsync(buffer.AsMemory(),cts.Token))>0) { text.Append(buffer,0,n); if(text.Length>2_000_000) text.Remove(0,text.Length-2_000_000); }
            return text.ToString();
        }
        var stdout = Drain(p.StandardOutput); var stderr = Drain(p.StandardError);
        try { await p.WaitForExitAsync(cts.Token); return new(p.ExitCode,await stdout,await stderr); }
        catch { try { p.Kill(true); } catch { } try { await Task.WhenAll(stdout,stderr); } catch { } throw; }
    }
}

public sealed record Track(string Type,int Width,int Height,int Channels,int SampleRate,double Duration,string Codec,string Layout,string PixelFormat,string Transfer);
public sealed record MediaInfo(double Duration,List<Track> Tracks,DateTimeOffset? RecordedAt,string Warnings);
public sealed class Media(string tools)
{
    public string Ffmpeg => Path.Combine(tools,"ffmpeg.exe");
    public string Ffprobe => Path.Combine(tools,"ffprobe.exe");
    public async Task<MediaInfo> Probe(string path,CancellationToken token)
    {
        var r=await Runner.Run(Ffprobe,["-v","warning","-show_format","-show_streams","-of","json",path],token,TimeSpan.FromMinutes(3));
        if(r.Code!=0) throw new IOException("Cannot read recording index. Missing video/index data may require specialist recovery. " + r.Errors);
        using var d=JsonDocument.Parse(r.Output); var root=d.RootElement;
        static string S(JsonElement e,string k) => e.TryGetProperty(k,out var v)? v.ToString():"";
        static double N(JsonElement e,string k) => double.TryParse(S(e,k),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)? n:0;
        var f=root.GetProperty("format"); var tracks=new List<Track>();
        DateTimeOffset? date=null;
        void ReadDate(JsonElement obj) { if(date==null && obj.TryGetProperty("tags",out var tags) && DateTimeOffset.TryParse(S(tags,"creation_time"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var dt)) date=dt; }
        ReadDate(f);
        foreach(var t in root.GetProperty("streams").EnumerateArray()) {
            ReadDate(t);
            var type=S(t,"codec_type");
            if(type is "video" or "audio") tracks.Add(new(type,(int)N(t,"width"),(int)N(t,"height"),(int)N(t,"channels"),(int)N(t,"sample_rate"),N(t,"duration"),S(t,"codec_name"),S(t,"channel_layout"),S(t,"pix_fmt"),S(t,"color_transfer")));
        }
        var duration=N(f,"duration");
        if(duration<=0 || !tracks.Any(x=>x.Type=="video")) throw new IOException("No usable video or recording duration was found.");
        return new(duration,tracks,date,r.Errors);
    }
    public async Task<RunResult> Decode(string path,CancellationToken token) => await Runner.Run(Ffmpeg,["-hide_banner","-nostdin","-v","error","-threads","2","-i",path,"-map","0:v","-map","0:a?","-f","null","-"],token);
    public async Task<bool> Clean(string path,CancellationToken token) { var r=await Decode(path,token); return r.Code==0 && string.IsNullOrWhiteSpace(r.Errors); }
    public static DateTimeOffset? RecordedTime(MediaInfo info,string path,string timeZone)
    {
        if(info.RecordedAt is { Year: >= 1990 } dt) return dt;
        var m=Regex.Match(Path.GetFileName(path),@"(?<date>\d{4}\.\d{2}\.\d{2}) - (?<time>\d{2}\.\d{2}\.\d{2})");
        if(!m.Success || !DateTime.TryParseExact(m.Groups["date"].Value+" "+m.Groups["time"].Value,"yyyy.MM.dd HH.mm.ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)) return null;
        var zone=TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        if(zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return null;
        return new DateTimeOffset(local,zone.GetUtcOffset(local));
    }
    public async Task Encode(string input,string output,bool lossless,DateTimeOffset? recorded,CancellationToken token)
    {
        if(!lossless) {
            var source=await Probe(input,token);
            if(source.Tracks.Any(t=>t.Channels>2 || (t.Type=="video" && (t.Transfer is "smpte2084" or "arib-std-b67" || t.PixelFormat!="yuv420p"))))
                throw new IOException("Automatic re-encoding is limited to SDR 8-bit video with mono/stereo audio. This recording needs specialist recovery; original kept.");
        }
        var args=new List<string>{"-hide_banner","-nostdin","-v","warning","-n","-threads","2","-i",input,"-map","0:v","-map","0:a?","-map_metadata","0"};
        if(lossless) args.AddRange(["-c","copy"]);
        else args.AddRange(["-c:v","libx264","-threads","2","-preset","fast","-crf","18","-pix_fmt","yuv420p","-c:a","aac","-b:a","192k"]);
        if(recorded!=null) {
            var utc=recorded.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture);
            args.AddRange(["-metadata","creation_time="+utc,"-metadata:s:v","creation_time="+utc,"-metadata:s:a","creation_time="+utc]);
        }
        args.AddRange(["-movflags","+faststart","-f","mp4",output]);
        var r=await Runner.Run(Ffmpeg,args,token);
        if(r.Code!=0) throw new IOException("Recovery engine could not finish: "+(r.Errors.Length>2500?r.Errors[^2500..]:r.Errors));
    }
    public static void VerifyShape(MediaInfo source,MediaInfo output)
    {
        if(Math.Abs(source.Duration-output.Duration)>0.25) throw new IOException("Repair changed the recording duration; original kept.");
        if(source.Tracks.Count!=output.Tracks.Count) throw new IOException("Repair lost a video/audio track; original kept.");
        var a=source.Tracks.OrderBy(t=>t.Type).ToArray(); var b=output.Tracks.OrderBy(t=>t.Type).ToArray();
        for(var i=0;i<a.Length;i++) {
            if(a[i].Type!=b[i].Type || a[i].Width!=b[i].Width || a[i].Height!=b[i].Height || a[i].Channels!=b[i].Channels || a[i].SampleRate!=b[i].SampleRate || (a[i].Duration>0 && Math.Abs(a[i].Duration-b[i].Duration)>0.25))
                throw new IOException("Repair changed track dimensions, audio channels, sample rate or duration; original kept.");
        }
    }
    public static async Task<string> Hash(string file,CancellationToken token) { using var f=File.OpenRead(file); return Convert.ToHexString(await SHA256.HashDataAsync(f,token)); }
}
