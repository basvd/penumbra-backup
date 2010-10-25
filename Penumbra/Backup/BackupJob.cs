using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using Alphaleonis.Win32.Filesystem;
using Penumbra.Misc;

namespace Penumbra
{
  
  public delegate bool DirExcludedCallback(BackupEntry e);
  public delegate bool FileExcludedCallback(BackupEntry e);
  public delegate MetadataEntry GetMetadataCallback();

  public class BackupJob
  {
    Config _cfg;
    Dictionary<string, ShadowCopy> _shadows;
    List<BackupEntry> _entries;
    Stopwatch _timer;
    long _progress;
    long _size;
    volatile bool _cancel;

    // Backup components
    ExclusionFilter _filter;
    BackupEngine _engine;
    List<IBackupExtension> _extensions;

    // API callbacks

    DirExcludedCallback _dirx;
    FileExcludedCallback _filex;
    GetMetadataCallback _meta;

    Config Config
    {
      get
      {
        return _cfg;
      }
    }

    public double Progress
    {
      get
      {
        if (_size == 0) return 0;
        return _progress / (double) _size;
      }
    }

    public BackupJob(Config c)
    {
      if (c == null) throw new ArgumentNullException();

      _timer = new Stopwatch();
      _cfg = c;
      
      // Check prerequisites
      if (_cfg.Sources == null || _cfg.Sources.Count == 0)
        throw new Exception("No sources specified!");

      if (_cfg.Options["target"] == null || _cfg.Options["target"].Trim() == "")
        throw new Exception("No target specified!");

      if (!Path.IsValidPath(_cfg.Target))
        throw new Exception("Invalid target specified!");

      if (StartShadowCopy())
        Console.WriteLine("Initializing Volume Shadow Copy service...");
      else
        throw new Exception("Volume Shadow Copy service is not running!");

      _filter = _cfg.Flags["incremental"] ? new IncrementalBackupFilter(_cfg) : new FullBackupFilter(_cfg);
      _engine = new ZipEngine(_cfg); // Only use zip for now
      _extensions = new List<IBackupExtension>();
      _extensions.Add(new SubInAclExtension(_cfg));
    }

    public void Start()
    {
      _cancel = false;
      _progress = 0;
      _size = 0;
      _timer.Reset();
      _timer.Start();
      try
      {
        _shadows = PrepareVolumes();
        if (_cancel) throw new BackupCanceledException();

        Console.WriteLine("Collecting backup entries...");
        GetEntries();
        if (_cancel) throw new BackupCanceledException();

        Console.WriteLine("Creating backup...");
        CreateBackup();
        Finish(true);
      }
      catch (Alphaleonis.Win32.Vss.VssException e)
      {
        Console.WriteLine(e.Message);
        Finish(false);
      }
      catch (BackupCanceledException)
      {
        Console.WriteLine("Backup cancelled...");
        Finish(false);
      }
      finally
      {
        if(_shadows != null) foreach (ShadowCopy vss in _shadows.Values)
        {
          vss.Dispose();
        }
      }
    }

    public void Stop()
    {
      _cancel = true;
    }

    void Finish(bool ok)
    {
      _timer.Stop();
      if (ok)
      {
        //Console.SetCursorPosition(0, Console.CursorTop);
        Console.WriteLine("Done!");
        Console.WriteLine("Backup file: " + _cfg.Target);
      }
      else
        Console.WriteLine("Backup failed!");

      string elapsed = _timer.Elapsed.ToString();
      elapsed = elapsed.Remove(elapsed.LastIndexOf("."));
      Console.WriteLine("Completed in: " + elapsed);
    }

    void CreateBackup()
    {
      if (_entries.Count == 0)
      {
        Console.WriteLine("There is nothing to back up.");
        return;
      }
      string targetDir = Path.GetDirectoryName(_cfg.Target);
      if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

      _engine.Init();
      foreach (BackupEntry file in _entries)
      {
        if (_cancel) throw new BackupCanceledException();
        try
        {
          _engine.Write(file);
          _progress += file.Info.FileSize / 1024;
          foreach (IBackupExtension e in _extensions) e.OnWrite(file);
        }
        catch (Exception e)
        {
          // TODO: More specific exception handling
          Console.WriteLine(e.Message);
        }
      }
      foreach (IBackupExtension e in _extensions)
      {
        if (_cancel) throw new BackupCanceledException();
        MetadataEntry meta = e.GetMetadata();
        if (meta != null) _engine.Write(meta);
      }
      _engine.Post();
    }

    // Replaces template tags in target
    string ParseTarget(string target)
    {
      if (target == null)
        return null;
      DateTime time = DateTime.Now;
      target += ".zip";
      return target
        .Replace("{name}", _cfg.Name)
        .Replace("{date}", time.ToString(_cfg.Options["date_format"]));
    }

    // Prepare volume snapshots
    Dictionary<string, ShadowCopy> PrepareVolumes()
    {
      HashSet<string> vols = new HashSet<string>();
      foreach (string p in _cfg.Sources)
      {
        vols.Add(Path.GetPathRoot(p));
      }
      Dictionary<string, ShadowCopy> vss = new Dictionary<string, ShadowCopy>(vols.Count);
      foreach (string v in vols)
      {
        // FIXME: One VssBackup per volume should not be necessary (?)
        Console.WriteLine("Preparing volume " + v + "...");
        ShadowCopy bkp = null;
        try
        {
          bkp = new ShadowCopy();
          bkp.Setup(v);
          vss.Add(v, bkp);
        }
        catch (Exception e)
        {
          if (bkp != null)
          {
            bkp.PreBackup();
            bkp.Dispose(false);
            bkp = null;
          }
          foreach (ShadowCopy b in vss.Values)
          {
            b.Dispose(false);
          }
          throw e;
        }
      }
      return vss;
    }

    // Lists all backup entries and applies filters
    void GetEntries()
    {
      _entries = new List<BackupEntry>();
      Queue<string> dirQ = new Queue<string>();
      foreach (string source in _cfg.Sources)
      {
        Console.WriteLine("Scanning source: " + source);

        string volRoot = Path.GetPathRoot(source);
        ShadowCopy vss = _shadows[volRoot];
        string snapSource = vss.GetSnapshotPath(source);
        string rootPath = vss.GetSnapshotPath(String.Empty);

        if (Directory.Exists(snapSource))
        {
          dirQ.Enqueue(snapSource);
          while (dirQ.Count > 0)
          {
            if (_cancel) throw new BackupCanceledException();

            string path = dirQ.Dequeue();
            try
            {
              // Subfolders
              string[] subdirs;
              if (_cfg.Flags["recursive"] && (subdirs = Directory.GetDirectories(path)).Length > 0)
                foreach (string dir in subdirs)
                {
                  BackupEntry entry = new BackupEntry(dir, vss);
                  if (!entry.Info.IsSymbolicLink && !entry.Info.IsReparsePoint && !_filter.IsDirExcluded(entry))
                  {
                    dirQ.Enqueue(dir);
                  }
                  entry.Dispose();
                }
              subdirs = null;

              // Files
              string[] files = Directory.GetFiles(path);
              if (files.Length > 0)
                foreach (string f in files)
                {
                  BackupEntry entry = new BackupEntry(f, vss);
                  if (!entry.CanRead || _filter.IsFileExcluded(entry))
                  {
                    entry.Dispose();
                    continue;
                  }
                  _entries.Add(entry);
                  _size += entry.Info.FileSize / 1024;
                }
              files = null;
            }
            catch (Exception e)
            {
              // TODO: More specific exception
              Console.WriteLine(e);
              Console.WriteLine(vss.GetRealPath(path) + " is not accessible.");
            }
          }
        }
        else if (File.Exists(snapSource))
        {
          BackupEntry entry = new BackupEntry(snapSource, vss);
          if (_filter.IsFileExcluded(entry))
            continue;
          _entries.Add(entry);
          _size += entry.Info.FileSize;
        }
      }
      GC.Collect();
    }

    // Checks for and, if necessary, attempts to start VSS
    static bool StartShadowCopy()
    {
      // Check for running VSS
      foreach (ServiceController service in ServiceController.GetServices())
      {
        if (service.ServiceName == "VSS")
        {
          if (service.Status != ServiceControllerStatus.Running)
          {
            Console.WriteLine("Attempting to start Volume Shadow Copy service...");
            try
            {
              service.Start();
              service.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(0, 0, 30));
              Console.WriteLine("Service started!");
            }
            catch (System.ServiceProcess.TimeoutException e)
            {
              Console.WriteLine(e.Message);
              return false;
            }
            service.Refresh();
          }
          return service.Status == ServiceControllerStatus.Running;
        }
      }
      return false;
    }
  }
}
