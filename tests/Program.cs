using ClipManager;

var tools=args[0];var testRoot=Path.GetFullPath(args[1]);Directory.CreateDirectory(testRoot);
var passed=0;
void Assert(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
var media=new Media(tools);var none=CancellationToken.None;
var fixture=Path.Combine(testRoot,"fixture.mp4");
var generated=await Runner.Run(media.Ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=320x180:rate=30","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-f","lavfi","-i","sine=frequency=880:sample_rate=48000","-t","3","-map","0:v","-map","1:a","-map","2:a","-c:v","libx264","-c:a","aac","-metadata","creation_time=2026-09-05T05:09:52Z",fixture],none);
Assert(generated.Code==0,"generate video with two audio tracks");
Assert(Paths.CleanName("test.DVR.mp4",false)=="test.mp4","remove DVR suffix");
Assert(Paths.CleanName("test.DVR.mp4",true)=="test repaired.mp4","repaired suffix");
Assert(Paths.CleanName("test repaired.mp4",true)=="test repaired.mp4","suffix is idempotent");
var clips=Path.Combine(testRoot,"clips");var backups=Path.Combine(testRoot,"backups");var cloud=Path.Combine(testRoot,"cloud");Directory.CreateDirectory(clips);Directory.CreateDirectory(cloud);
var settings=new Settings{ClipsFolder=clips,BackupFolder=backups,ICloudFolder=cloud,ICloudEnabled=true};
var store=new Store(Path.Combine(testRoot,"state"));var engine=new Engine(store,media);engine.Changed+=j=>Console.WriteLine("  "+j.State+": "+j.Detail);
var source=Path.Combine(clips,"example.DVR.mp4");File.Copy(fixture,source);var hash=await Media.Hash(source,none);
var job=await engine.Process(source,settings,none);
Assert(job.State=="Completed","normalize healthy recording");
Assert(!File.Exists(source)&&File.Exists(job.Output),"original replaced by regular MP4");
Assert(await Media.Hash(job.Backup,none)==hash,"original backup is byte-identical");
var probe=await media.Probe(job.Output,none);
Assert(probe.Tracks.Count==3,"all audio tracks retained");
Assert(probe.RecordedAt==DateTimeOffset.Parse("2026-09-05T05:09:52Z"),"recording date preserved");
Assert(job.CloudState=="Queued to iCloud" && File.Exists(job.CloudPath),"iCloud folder handoff (local simulation)");
await engine.ExportCloud(job,settings,none);
Assert(Directory.GetFiles(cloud,"*.mp4").Length==1,"no duplicate cloud handoff");
engine.Restore(job);
Assert(await Media.Hash(source,none)==hash,"restore original from backup");
var collision=Path.Combine(clips,"example.mp4");var collisionHash=await Media.Hash(collision,none);
var second=await engine.Process(source,settings,none);
Assert(second.Output!=collision && await Media.Hash(collision,none)==collisionHash,"filename collision never overwrites");
var bad=Path.Combine(clips,"incomplete.DVR.mp4");await File.WriteAllBytesAsync(bad,[0,0,0,12,102,116,121,112,105,115,111,109]);var badHash=await Media.Hash(bad,none);
var failed=await engine.Process(bad,settings,none);
Assert(failed.State=="Needs attention" && await Media.Hash(bad,none)==badHash,"unrecoverable clip is not replaced");
var before=probe;
var shortInfo=before with{Duration=1};bool rejected=false;try{Media.VerifyShape(before,shortInfo);}catch(IOException){rejected=true;}
Assert(rejected,"reject shortened repair");
rejected=false;try{Media.VerifyShape(before,before with{Tracks=before.Tracks.Take(1).ToList()});}catch(IOException){rejected=true;}
Assert(rejected,"reject missing audio track");
rejected=false;try{new Settings{ClipsFolder=clips,BackupFolder=Path.Combine(clips,"backup")}.Validate();}catch(IOException){rejected=true;}
Assert(rejected,"reject backup folder inside watched folder");
var crashSource=Path.Combine(clips,"crash.DVR.mp4");var crashBackup=Path.Combine(backups,"crash.mp4");File.Copy(fixture,crashBackup);
var interrupted=new Job{Source=crashSource,Backup=crashBackup,State="Backed up",SourceHash=hash};store.Save(interrupted);
await engine.RecoverInterrupted(none);
Assert(File.Exists(crashSource)&&await Media.Hash(crashSource,none)==hash,"startup rolls back interrupted replacement");
var plain=Path.Combine(clips,"plain.mp4");File.Copy(fixture,plain);var plainHash=await Media.Hash(plain,none);
var unchanged=await engine.Process(plain,settings,none);
Assert(unchanged.State=="Unchanged"&&await Media.Hash(plain,none)==plainHash,"healthy regular MP4 untouched");
using(var locked=new FileStream(plain,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
    var lockedJob=await engine.Process(plain,settings,none);Assert(lockedJob.State=="Needs attention","cannot process a file NVIDIA still holds open");
}
using(var cts=new CancellationTokenSource(200)) {
    var cancelled=false;try{await Runner.Run(media.Ffmpeg,["-re","-f","lavfi","-i","testsrc=size=64x64","-t","30","-f","null","-"],cts.Token);}catch(OperationCanceledException){cancelled=true;}
    Assert(cancelled,"cancel stops a running media process");
}
// Damage one non-key video packet without damaging the MP4 index or changing timestamps.
var damagedPath=Path.Combine(clips,"damaged-packet.DVR.mp4");File.Copy(fixture,damagedPath);
var packets=await Runner.Run(media.Ffprobe,["-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size","-of","json",damagedPath],none);
using(var packetJson=System.Text.Json.JsonDocument.Parse(packets.Output)){
    var packet=packetJson.RootElement.GetProperty("packets")[20];
    var position=long.Parse(packet.GetProperty("pos").GetString()!);
    var size=int.Parse(packet.GetProperty("size").GetString()!);
    using var f=new FileStream(damagedPath,FileMode.Open,FileAccess.Write);
    f.Position=position+Math.Min(20,size/2);f.Write(new byte[Math.Max(1,size-Math.Min(20,size/2))]);
}
var damageHash=await Media.Hash(damagedPath,none);
var repair=await engine.Process(damagedPath,settings,none);
Assert(repair.State=="Completed" && repair.Repaired,"repair damaged video packet through verified re-encoding");
Assert(await Media.Hash(repair.Backup,none)==damageHash && await media.Clean(repair.Output,none),"damaged original preserved and repair decodes cleanly");

// The polling watcher must ignore baseline clips and process a new file exactly once.
var watchRoot=Path.Combine(testRoot,"watch");Directory.CreateDirectory(watchRoot);
var oldClip=Path.Combine(watchRoot,"old.DVR.mp4");File.Copy(fixture,oldClip);
var watchStore=new Store(Path.Combine(testRoot,"watch-state"));var watchEngine=new Engine(watchStore,media);
var watchSettings=new Settings{ClipsFolder=watchRoot,BackupFolder=Path.Combine(testRoot,"watch-backups")};
using(var watchCancel=new CancellationTokenSource(TimeSpan.FromSeconds(50))){
    var watcher=new Watcher(watchStore,watchEngine);var loop=watcher.Run(watchSettings,false,watchCancel.Token);
    await Task.Delay(500);
    var fresh=Path.Combine(watchRoot,"fresh.DVR.mp4");File.Copy(fixture,fresh);File.SetLastWriteTimeUtc(fresh,DateTime.UtcNow.AddMinutes(-1));
    while(!File.Exists(Path.Combine(watchRoot,"fresh.mp4"))&&!watchCancel.IsCancellationRequested)await Task.Delay(500);
    watchCancel.Cancel();try{await loop;}catch(OperationCanceledException){}
    Assert(File.Exists(oldClip),"watcher excludes preexisting library until requested");
    Assert(File.Exists(Path.Combine(watchRoot,"fresh.mp4")),"new clip automatically normalized after settling");
    Assert(watchStore.Jobs().Count(j=>j.State=="Completed")==1,"watcher processes new clip exactly once");
}
var secondWatchRoot=Path.Combine(testRoot,"another-watch-folder");Directory.CreateDirectory(secondWatchRoot);
var otherOld=Path.Combine(secondWatchRoot,"existing.DVR.mp4");File.Copy(fixture,otherOld);File.SetLastWriteTimeUtc(otherOld,DateTime.UtcNow.AddMinutes(-1));
watchSettings.ClipsFolder=secondWatchRoot;
using(var cancelSecond=new CancellationTokenSource(TimeSpan.FromSeconds(18))){
    try{await new Watcher(watchStore,watchEngine).Run(watchSettings,false,cancelSecond.Token);}catch(OperationCanceledException){}
}
Assert(File.Exists(otherOld)&&!File.Exists(Path.Combine(secondWatchRoot,"existing.mp4")),"changing watched folders does not automatically process its old library");
if(args.Length>2){
    var real=Path.Combine(clips,"actual-sample.DVR.mp4");File.Copy(args[2],real);var original=await Media.Hash(real,none);
    settings.ICloudEnabled=false;
    var recovered=await engine.Process(real,settings,none);
    if(recovered.State=="Completed") {
        Assert(recovered.Repaired,"real NVIDIA sample marked as repaired");
        Assert(await Media.Hash(recovered.Backup,none)==original,"real sample original retained");
        Assert(await media.Clean(recovered.Output,none),"real sample full decode passes");
        Console.WriteLine("REAL_OUTPUT="+recovered.Output);
    } else {
        Assert(recovered.State=="Needs attention" && await Media.Hash(real,none)==original,"extensively damaged real recording safely retained");
        Assert(!File.Exists(recovered.Output),"no incomplete replacement published");
    }
}
Console.WriteLine($"All {passed} checks passed. Test files: {testRoot}");
