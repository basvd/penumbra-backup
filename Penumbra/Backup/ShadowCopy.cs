using System;
using System.Diagnostics;
using Alphaleonis.Win32.Filesystem;
using Alphaleonis.Win32.Vss;

namespace Penumbra
{
  public class ShadowCopy
  {
    IVssBackupComponents _backup;
    VssSnapshotProperties _props;
    Guid _set_id;
    Guid _snap_id;

    public string DriveLetter;

    public ShadowCopy()
    {
      InitializeBackup();
    }

    public void Setup(string volumeName)
    {
      Discovery(volumeName);
      PreBackup();
    }

    public void Dispose(bool ok)
    {
      try { Complete(ok); Delete(); }
      catch { }

      if (_backup != null)
      {
        _backup.Dispose();
        _backup = null;
      }
    }
    public void Dispose()
    {
      Dispose(true);
    }

    void InitializeBackup()
    {
      IVssImplementation vss = VssUtils.LoadImplementation();
      _backup = vss.CreateVssBackupComponents();
      _backup.InitializeForBackup(null);

      using (IVssAsync async = _backup.GatherWriterMetadata())
      {
        async.Wait();
      }
    }
    void Discovery(string fullPath)
    {
      _backup.FreeWriterMetadata();

      _set_id = _backup.StartSnapshotSet();
      AddVolume(Path.GetPathRoot(fullPath));
    }

    public void PreBackup()
    {
      Debug.Assert(_set_id != null);

      _backup.SetBackupState(false,
            true, VssBackupType.Full, false);

      using (IVssAsync async = _backup.PrepareForBackup())
      {
        async.Wait();
      }
      Copy();
    }

    public string GetSnapshotPath(string localPath)
    {
      Trace.WriteLine("New volume: " + Root);

      // This bit replaces the entry's normal root information with root
      // info from our new shadow copy.
      if (Path.IsPathRooted(localPath))
      {
        string root = Path.GetPathRoot(localPath);
        localPath = localPath.Remove(0, root.Length);
      }
      string slash = Path.DirectorySeparatorChar.ToString();
      if (!Root.EndsWith(slash) && !localPath.StartsWith(slash))
        localPath = localPath.Insert(0, slash);
      localPath = localPath.Insert(0, Root);

      Trace.WriteLine("Converted path: " + localPath);

      return localPath;
    }

    public string GetRealPath(string vssPath)
    {
      if (vssPath.StartsWith(Root))
      {
        vssPath = vssPath.Remove(0, Root.Length + 1);
      }
      string slash = Path.DirectorySeparatorChar.ToString();
      if (!DriveLetter.EndsWith(slash) && !vssPath.StartsWith(slash))
        vssPath = vssPath.Insert(0, slash);
      vssPath = vssPath.Insert(0, DriveLetter);

      Trace.WriteLine("Converted path: " + vssPath);

      return vssPath;
    }

    public System.IO.Stream GetStream(string localPath)
    {
      return File.OpenBackupRead(GetSnapshotPath(localPath));
    }

    public void Complete(bool succeeded)
    {
      try
      {
        // The BackupComplete event must be sent to all of the writers.
        using (IVssAsync async = _backup.BackupComplete())
          async.Wait();
      }
      catch (VssBadStateException) { }
    }

    string FileToPathSpecification(VssWMFileDescription file)
    {
      // Environment variables (eg. "%windir%") are common.
      string path = Environment.ExpandEnvironmentVariables(file.Path);

      // Use the alternate location if it's present.
      if (!String.IsNullOrEmpty(file.AlternateLocation))
        path = Environment.ExpandEnvironmentVariables(
              file.AlternateLocation);

      // Normalize wildcard usage.
      string spec = file.FileSpecification.Replace("*.*", "*");

      // Combine the entry specification and the directory name.
      return Path.Combine(path, file.FileSpecification);
    }

    public void AddVolume(string volumeName)
    {
      if (_backup.IsVolumeSupported(volumeName))
      {
        DriveLetter = volumeName;
        _snap_id = _backup.AddToSnapshotSet(volumeName);
      }
      else
        throw new VssVolumeNotSupportedException(volumeName);
    }

    public void Copy()
    {
      using (IVssAsync async = _backup.DoSnapshotSet())
        async.Wait();
    }

    public void Delete()
    {
      _backup.DeleteSnapshotSet(_set_id, false);
    }

    public string Root
    {
      get
      {
        if (_props == null)
          _props = _backup.GetSnapshotProperties(_snap_id);
        return _props.SnapshotDeviceObject;
      }
    }
  }
}
