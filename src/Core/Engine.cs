namespace ClipManager;

public sealed class Engine(Store store,Media media)
{
    public event Action<Job>? Changed;
    private void Update(Job job,string state,string detail="") { job.State=state; job.Detail=detail; store.Save(job); Changed?.Invoke(job); }
    public async Task<Job> Process(string path,Settings settings,CancellationToken token)
    {
        settings.Validate();
        Paths.NoLinks(path); Paths.NoLinks(settings.BackupFolder);
        if(!Paths.Inside(path,settings.ClipsFolder)) throw new IOException("Clip is outside the selected folder.");
        var job=new Job{Source=Path.GetFullPath(path),CloudRequested=settings.ICloudEnabled};
        Update(job,"Checking","Reading every video and audio packet.");
        FileStream? guard=null;
        try {
            guard=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            var originalCreated=File.GetCreationTimeUtc(path); var originalWritten=File.GetLastWriteTimeUtc(path);
            var source=await media.Probe(path,token);
            job.RecordedAt=Media.RecordedTime(source,path,settings.RecordingTimeZone);
            job.SourceHash=await Media.Hash(path,token);
            var decode=await media.Decode(path,token);
            var damaged=decode.Code!=0 || !string.IsNullOrWhiteSpace(decode.Errors) || !string.IsNullOrWhiteSpace(source.Warnings);
            job.Repaired=damaged;
            var proposed=Path.Combine(Path.GetDirectoryName(path)!,Paths.CleanName(path,damaged));
            if(!damaged && string.Equals(path,proposed,StringComparison.OrdinalIgnoreCase)) {
                job.Output=path; job.OutputHash=job.SourceHash;
                Update(job,"Unchanged","Full decode passed; already a regular MP4.");
            } else {
                job.Output=Paths.Available(proposed);
                job.Temporary=Path.Combine(Path.GetDirectoryName(path)!,".ncm-"+job.Id+".partial");
                Update(job,damaged?"Repairing":"Normalizing",damaged?"Trying lossless container repair first.":"Preserving quality and the recording timestamp.");
                try {
                    await media.Encode(path,job.Temporary,true,job.RecordedAt,token);
                    Media.VerifyShape(source,await media.Probe(job.Temporary,token));
                    if(!await media.Clean(job.Temporary,token)) throw new IOException("Lossless repair still has decoder errors.");
                } catch(Exception ex) when(ex is not OperationCanceledException) {
                    if(File.Exists(job.Temporary)) File.Delete(job.Temporary);
                    if(!damaged) throw;
                    Update(job,"Repairing","Re-encoding video and preserving every audio track.");
                    await media.Encode(path,job.Temporary,false,job.RecordedAt,token);
                }
                Update(job,"Verifying","Checking the entire result, track lengths and recording date.");
                var output=await media.Probe(job.Temporary,token);
                Media.VerifyShape(source,output);
                if(job.RecordedAt!=null && (output.RecordedAt==null || Math.Abs((output.RecordedAt.Value-job.RecordedAt.Value).TotalSeconds)>1)) throw new IOException("Recording date did not survive repair.");
                if(!await media.Clean(job.Temporary,token)) throw new IOException("Repaired copy still reports decoder errors; original kept.");
                job.OutputHash=await Media.Hash(job.Temporary,token);
                File.SetCreationTimeUtc(job.Temporary,originalCreated); File.SetLastWriteTimeUtc(job.Temporary,originalWritten);
                var relative=Path.GetRelativePath(settings.ClipsFolder,path);
                job.Backup=Path.Combine(settings.BackupFolder,job.Id,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(job.Backup)!);
                // Durable journal precedes every step that can change the original name.
                Update(job,"Prepared","Verified replacement ready; backing up original.");
                token.ThrowIfCancellationRequested();
                guard.Dispose(); guard=null;
                if(await Media.Hash(path,token)!=job.SourceHash) throw new IOException("NVIDIA changed the clip during processing; original kept.");
                File.Move(path,job.Backup,false);
                if(await Media.Hash(job.Backup,CancellationToken.None)!=job.SourceHash) throw new IOException("Backup hash mismatch; no replacement published.");
                Update(job,"Backed up");
                File.Move(job.Temporary,job.Output,false);
                Update(job,"Completed",damaged?"Repaired and fully verified. Original is in backups.":"Normalized to .mp4 without re-encoding. Original is in backups.");
            }
            guard?.Dispose(); guard=null;
            if(job.CloudRequested) await ExportCloud(job,settings,token);
        } catch(Exception ex) {
            guard?.Dispose(); guard=null;
            if(!File.Exists(job.Source) && File.Exists(job.Backup)) {
                try { File.Copy(job.Backup,job.Source,false); } catch(Exception rollback) { ex=new IOException(ex.Message+" Restore needed: "+rollback.Message,ex); }
            }
            // Only this job's temporary output is removed. User originals/backups are never deleted.
            if(File.Exists(job.Temporary)) { try { File.Delete(job.Temporary); } catch { } }
            Update(job,ex is OperationCanceledException?"Cancelled":"Needs attention",ex.Message);
        } finally { guard?.Dispose(); }
        return job;
    }

    public async Task ExportCloud(Job job,Settings settings,CancellationToken token)
    {
        if(!settings.ICloudEnabled || !job.CloudRequested || job.CloudState=="Queued to iCloud") return;
        try {
            settings.Validate();
            if(job.RecordedAt==null) throw new IOException("Recording date is unknown or ambiguous. iCloud export paused to avoid the wrong date.");
            if(!File.Exists(job.Output) || await Media.Hash(job.Output,token)!=job.OutputHash) throw new IOException("Local output changed; export paused.");
            job.CloudState="Preparing iCloud copy"; store.Save(job); Changed?.Invoke(job);
            var cloudWork=Path.Combine(store.Root,"exports"); Directory.CreateDirectory(cloudWork);
            var temporary=Path.Combine(cloudWork,job.Id+".mp4");
            if(File.Exists(temporary)) File.Delete(temporary);
            var source=await media.Probe(job.Output,token);
            // H.264/AAC is a conservative iPhone-compatible export. Main library remains untouched.
            var compatible=source.Tracks.All(x=>x.Type=="video"? x.Codec=="h264":x.Codec=="aac");
            await media.Encode(job.Output,temporary,compatible,job.RecordedAt,token);
            var cloudInfo=await media.Probe(temporary,token);
            Media.VerifyShape(source,cloudInfo);
            if(cloudInfo.RecordedAt==null || Math.Abs((cloudInfo.RecordedAt.Value-job.RecordedAt.Value).TotalSeconds)>1 || !await media.Clean(temporary,token)) throw new IOException("iCloud copy failed validation.");
            // Deterministic hash filename avoids repeat uploads across restarts, even if an earlier file becomes a placeholder.
            var shortHash=job.OutputHash[..16].ToLowerInvariant();
            var destination=Path.Combine(settings.ICloudFolder,Path.GetFileNameWithoutExtension(job.Output)+" - "+shortHash+".mp4");
            job.CloudPath=destination; store.Save(job);
            if(File.Exists(destination) && await Media.Hash(destination,token)!=await Media.Hash(temporary,token))
                throw new IOException("An existing iCloud file has different contents; it was not overwritten.");
            if(!File.Exists(destination)) {
                var staging=destination+".ncm-partial";
                if(File.Exists(staging)) File.Delete(staging);
                File.Copy(temporary,staging,false);
                if(await Media.Hash(staging,token)!=await Media.Hash(temporary,token)) throw new IOException("iCloud copy verification failed.");
                File.SetCreationTimeUtc(staging,job.RecordedAt.Value.UtcDateTime); File.SetLastWriteTimeUtc(staging,job.RecordedAt.Value.UtcDateTime);
                File.Move(staging,destination,false);
            }
            File.Delete(temporary);
            job.CloudState="Queued to iCloud"; store.Save(job); Changed?.Invoke(job);
        } catch(Exception ex) {
            job.CloudState="Export pending: "+ex.Message; store.Save(job); Changed?.Invoke(job);
        }
    }

    public async Task RecoverInterrupted(CancellationToken token)
    {
        foreach(var job in store.Jobs().Where(j=> j.State is "Prepared" or "Backed up" or "Checking" or "Repairing" or "Normalizing" or "Verifying")) {
            if(File.Exists(job.Backup) && !File.Exists(job.Source)) {
                if(File.Exists(job.Output) && job.OutputHash.Length>0 && await Media.Hash(job.Output,token)==job.OutputHash)
                    Update(job,"Completed","Recovered completed transaction after an interrupted shutdown.");
                else { File.Copy(job.Backup,job.Source,false); Update(job,"Needs attention","Interrupted repair rolled back. Original restored from backup."); }
            } else Update(job,"Needs attention","Interrupted before replacement. Original retained; retry when ready.");
        }
    }
    public void Restore(Job job)
    {
        if(!File.Exists(job.Backup)) throw new IOException("This job has no available original backup.");
        if(File.Exists(job.Source)) throw new IOException("Original filename already exists. Nothing was overwritten.");
        File.Copy(job.Backup,job.Source,false);
        Update(job,"Restored","Original restored. Repaired copy and backup both kept.");
    }
}

public sealed class Watcher(Store store,Engine engine)
{
    private readonly Dictionary<string,(long Length,long Ticks,int Stable)> observations = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string,string> seen = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? Status;
    public static string Stamp(string path) { var f=new FileInfo(path); return f.Length+":"+f.LastWriteTimeUtc.Ticks; }
    private static IEnumerable<string> Files(string root) => Directory.EnumerateFiles(root,"*",new EnumerationOptions { RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint }).Where(p=>Path.GetExtension(p).Equals(".mp4",StringComparison.OrdinalIgnoreCase));
    public async Task Run(Settings settings,bool includeExisting,CancellationToken token)
    {
        settings.Validate(); Paths.NoLinks(settings.ClipsFolder); Paths.NoLinks(settings.BackupFolder);
        await engine.RecoverInterrupted(token);
        var seenPath=Path.Combine(store.Root,"seen.json");
        if(File.Exists(seenPath)) seen=new(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(seenPath))!,StringComparer.OrdinalIgnoreCase);
        else if(!includeExisting) foreach(var file in Files(settings.ClipsFolder)) seen[file]=Stamp(file);
        if(includeExisting) seen.Clear();
        // Never process our already-published outputs or restored originals again automatically.
        foreach(var job in store.Jobs()) {
            if(File.Exists(job.Output) && job.State is "Completed" or "Unchanged" or "Restored") seen[job.Output]=Stamp(job.Output);
            if(job.State=="Restored" && File.Exists(job.Source)) seen[job.Source]=Stamp(job.Source);
        }
        Store.Atomic(seenPath,seen);
        var cloudRetry=DateTimeOffset.MinValue;
        while(!token.IsCancellationRequested) {
            foreach(var path in Files(settings.ClipsFolder).ToList()) {
                token.ThrowIfCancellationRequested();
                try {
                    var f=new FileInfo(path); var stamp=Stamp(path);
                    if(seen.TryGetValue(path,out var old) && old==stamp) continue;
                    var key=observations.GetValueOrDefault(path);
                    var stable=key.Length==f.Length && key.Ticks==f.LastWriteTimeUtc.Ticks?key.Stable+1:0;
                    observations[path]=(f.Length,f.LastWriteTimeUtc.Ticks,stable);
                    if(stable<2 || DateTime.UtcNow-f.LastWriteTimeUtc<TimeSpan.FromSeconds(10)) continue;
                    try { using var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read); } catch(IOException) { continue; }
                    var job=await engine.Process(path,settings,token);
                    if(job.State!="Cancelled") seen[path]=stamp;
                    if(File.Exists(job.Output) && job.State is "Completed" or "Unchanged") seen[job.Output]=Stamp(job.Output);
                    Store.Atomic(seenPath,seen);
                } catch(OperationCanceledException) { throw; }
                catch(Exception ex) { Status?.Invoke(ex.Message); }
            }
            if(settings.ICloudEnabled && DateTimeOffset.UtcNow-cloudRetry>TimeSpan.FromMinutes(5)) {
                cloudRetry=DateTimeOffset.UtcNow;
                foreach(var job in store.Jobs().Where(j=>j.CloudRequested && j.State is "Completed" or "Unchanged" && j.CloudState!="Queued to iCloud")) await engine.ExportCloud(job,settings,token);
            }
            Status?.Invoke("Watching • waiting for finished clips");
            await Task.Delay(TimeSpan.FromSeconds(5),token);
        }
    }
}
