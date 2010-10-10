using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Alphaleonis.Win32.Filesystem;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;

namespace Penumbra
{
  public class BackupJob
  {
    Config _cfg;
    Dictionary<string, ShadowCopy> _shadows;
    List<BackupEntry> _entries;
    Stopwatch _timer;
    long _progress;
    long _size;

    // Backup components
    ExclusionFilter _filter;
    BackupEngine _engine;
    List<IBackupExtension> _extensions;

    public Config Config
    {
      get
      {
        return _cfg;
      }
    }

    public BackupJob(Config c)
    {
      if (c == null) throw new ArgumentNullException();

      _timer = new Stopwatch();
      _cfg = c;
      _size = 0;

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

    public void Run()
    {
      _timer.Start();
      bool ok = true;
      try
      {
        _shadows = PrepareVolumes();

        Console.WriteLine("Collecting backup entries...");
        GetEntries();

        Console.WriteLine("Creating backup...");
        ok = CreateBackup();
      }
      catch (Alphaleonis.Win32.Vss.VssException e)
      {
        ok = false;
        Console.WriteLine(e.Message);
      }
      finally
      {
        if(_shadows != null) foreach (ShadowCopy vss in _shadows.Values)
        {
          vss.Dispose(ok);
        }
      }
      _timer.Stop();

      if (ok)
      {
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.WriteLine("Done!");
        Console.WriteLine("Backup file: " + _cfg.Target);
      }
      else
      {
        Console.WriteLine("Backup failed!");
        return;
      }

      string elapsed = _timer.Elapsed.ToString();
      elapsed = elapsed.Remove(elapsed.LastIndexOf("."));
      Console.WriteLine("Completed in: " + elapsed);
    }

    protected bool CreateBackup()
    {
      if (_entries.Count == 0)
      {
        Console.WriteLine("There is nothing to back up.");
        return false;
      }
      string targetDir = Path.GetDirectoryName(_cfg.Target);
      if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

      _engine.Init();
      foreach (BackupEntry file in _entries)
      {
        try
        {
          _engine.Write(file);
          foreach (IBackupExtension e in _extensions) e.OnWrite(file);
        }
        catch (Exception e)
        {
          Console.WriteLine(e.Message);
        }
      }
      foreach (IBackupExtension e in _extensions)
      {
        MetadataEntry meta = e.GetMetadata();
        if (meta != null) _engine.Write(meta);
      }
      return _engine.Post();
    }

    // Replaces template tags in target
    protected string ParseTarget(string target)
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
    protected Dictionary<string, ShadowCopy> PrepareVolumes()
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
    protected void GetEntries()
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
                  if (_filter.IsFileExcluded(entry))
                  {
                    entry.Dispose();
                    continue;
                  }
                  _entries.Add(entry);
                  _size += entry.Info.FileSize;
                }
              files = null;
            }
            catch (Exception e)
            {
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
