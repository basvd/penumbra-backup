using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Alphaleonis.Win32.Filesystem;
using Alphaleonis.Win32.Vss;

namespace Penumbra
{
  public class VssBackup : IDisposable
  {
    /// <summary>A reference to the VSS context.</summary>
    IVssBackupComponents _backup;

    /// <summary>Some persistent context for the current snapshot.</summary>
    Snapshot _snap;

    /// <summary>
    /// Constructs a VssBackup object and initializes some of the necessary
    /// VSS structures.
    /// </summary>
    public VssBackup()
    {
      InitializeBackup();
    }

    /// <summary>
    /// Sets up a shadow copy against the specified volume.
    /// </summary>
    /// <remarks>
    /// This methods is separated out from the constructor because if it
    /// throws, we still want the Dispose() method to be called.
    /// </remarks>
    /// <param name="volumeName">Name of the volume to copy.</param>
    public void Setup(string volumeName)
    {
      Discovery(volumeName);
      PreBackup();
    }

    /// <summary>
    /// The disposal of this object involves sending completion notices
    /// to the writers, removing the shadow copies from the system and
    /// finally releasing the BackupComponents object.  This method must
    /// be called when this class is no longer used.
    /// </summary>
    public void Dispose(bool ok)
    {
      try { Complete(ok); }
      catch { }

      if (_snap != null)
      {
        _snap.Dispose();
        _snap = null;
      }

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

    /// <summary>
    /// This stage initializes both the requester (this program) and 
    /// any writers on the system in preparation for a backup and sets
    /// up a communcation channel between the two.
    /// </summary>
    void InitializeBackup()
    {
      // Here we are retrieving an OS-dependent object that encapsulates
      // all of the VSS functionality.  The OS indepdence that this single
      // factory method provides is one of AlphaVSS's major strengths!
      IVssImplementation vss = VssUtils.LoadImplementation();
      
      // Now we create a BackupComponents object to manage the backup.
      // This object will have a one-to-one relationship with its backup
      // and must be cleaned up when the backup finishes (ie. it cannot
      // be reused).
      //
      // Note that this object is a member of our class, as it needs to
      // stick around for the full backup.
      _backup = vss.CreateVssBackupComponents();

      // Now we must initialize the components.  We can either start a
      // fresh backup by passing null here, or we could resume a previous
      // backup operation through an earlier use of the SaveXML method.
      _backup.InitializeForBackup(null);

      // At this point, we're supposed to establish communication with
      // the writers on the system.  It is possible before this step to
      // enable or disable specific writers via the BackupComponents'
      // Enable* and Disable* methods.
      //
      // Note the 'using' construct here to dispose of the asynchronous
      // comm link once we no longer need it.
      using (IVssAsync async = _backup.GatherWriterMetadata())
      {
        // Because allowing writers to prepare their metadata can take
        // a while, we are given a VssAsync object that gives us some
        // status on the background operation.  In this case, we just
        // wait for it to finish.
        async.Wait();
      }
    }

    /// <summary>
    /// This stage involes the requester (us, again) processing the
    /// information it received from writers on the system to find out
    /// which volumes - if any - must be shadow copied to perform a full
    /// backup.
    /// </summary>
    void Discovery(string fullPath)
    {
      // Once we are finished with the writer metadata, we can dispose
      // of it.  If we were in component mode, we would want to keep it
      // around so that we could notify the writers of our success or
      // failure when we finish the backup.
      _backup.FreeWriterMetadata();

      // Now we use our helper class to add the appropriate volume to the
      // shadow copy set.
      _snap = new Snapshot(_backup);
      _snap.AddVolume(Path.GetPathRoot(fullPath));
    }

    /// <summary>
    /// This phase of the backup is focused around creating the shadow copy.
    /// We will notify writers of the impending snapshot, after which they
    /// have a short period of time to get their on-disk data in order and
    /// then quiesce writing.
    /// </summary>
    public void PreBackup()
    {
      Debug.Assert(_snap != null);

      // This next bit is a way to tell writers just what sort of backup
      // they should be preparing for.  The important parts for us now
      // are the first and third arguments: we want to do a full,
      // backup and, depending on whether we are in component mode, either
      // a full-volume backup or a backup that only requires specific
      // components.
      _backup.SetBackupState(false,
            true, VssBackupType.Full, false);

      // From here we just need to send messages to each writer that our
      // snapshot is imminent,
      using (IVssAsync async = _backup.PrepareForBackup())
      {
        // As before, the 'using' statement automatically disposes of
        // our comm link.  Also as before, we simply block while the
        // writers to complete their background preparations.
        async.Wait();
      }

      // It's now time to create the snapshot.  Each writer will have to
      // freeze its I/O to the selected volumes for up to 10 seconds
      // while this process takes place.
      _snap.Copy();
    }

    /// <summary>
    /// This simple method uses a bit of string manipulation to turn a
    /// full, local path into its corresponding snapshot path.  This
    /// method may help users perform full file copies from the snapsnot.
    /// </summary>
    /// <remarks>
    /// Note that the System.IO methods are not able to access files on
    /// the snapshot.  Instead, you will need to use the AlphaFS library
    /// as shown in the example.
    /// </remarks>
    /// <example>
    /// This code creates a shadow copy and copies a single file from
    /// the new snapshot to a location on the D drive.  Here we're
    /// using the AlphaFS library to make a full-file copy of the file.
    /// <code>
    /// string source_file = @"C:\Windows\system32\config\sam";
    /// string backup_root = @"D:\Backups";
    /// string backup_path = Path.Combine(backup_root,
    ///       Path.GetFilename(source_file));
    ///
    /// // Initialize the shadow copy subsystem.
    /// using (VssBackup vss = new VssBackup())
    /// {
    ///    vss.Setup(Path.GetPathRoot(source_file));
    ///    string snap_path = vss.GetSnapshotPath(source_file);
    /// 
    ///    // Here we use the AlphaFS library to make the copy.
    ///    Alphaleonis.Win32.Filesystem.File.Copy(snap_path, backup_path);
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="GetStream"/>
    /// <param name="localPath">The full path of the original file.</param>
    /// <returns>A full path to the same file on the snapshot.</returns>
    public string GetSnapshotPath(string localPath)
    {
      Trace.WriteLine("New volume: " + _snap.Root);

      // This bit replaces the file's normal root information with root
      // info from our new shadow copy.
      if (Path.IsPathRooted(localPath))
      {
        string root = Path.GetPathRoot(localPath);
        localPath = localPath.Remove(0, root.Length);
      }
      string slash = Path.DirectorySeparatorChar.ToString();
      if (!_snap.Root.EndsWith(slash) && !localPath.StartsWith(slash))
        localPath = localPath.Insert(0, slash);
      localPath = localPath.Insert(0, _snap.Root);

      Trace.WriteLine("Converted path: " + localPath);

      return localPath;
    }

    public string GetRealPath(string vssPath)
    {
      if (vssPath.StartsWith(_snap.Root))
      {
        vssPath = vssPath.Remove(0, _snap.Root.Length + 1);
      }
      string slash = Path.DirectorySeparatorChar.ToString();
      if (!_snap.DriveLetter.EndsWith(slash) && !vssPath.StartsWith(slash))
        vssPath = vssPath.Insert(0, slash);
      vssPath = vssPath.Insert(0, _snap.DriveLetter);

      Trace.WriteLine("Converted path: " + vssPath);

      return vssPath;
    }

    /// <summary>
    /// This method opens a stream over the shadow copy of the specified
    /// file.
    /// </summary>
    /// <example>
    /// This code creates a shadow copy and opens a stream over a file
    /// on the new snapshot volume.
    /// <code>
    /// string source_file = @"C:\Windows\system32\config\sam";
    /// 
    /// // Initialize the shadow copy subsystem.
    /// using (VssBackup vss = new VssBackup())
    /// {
    ///    vss.Setup(Path.GetPathRoot(filename));
    ///    
    ///    // We can now access the shadow copy by either retrieving a stream:
    ///    using (Stream s = vss.GetStream(filename))
    ///    {
    ///       Debug.Assert(s.CanRead == true);
    ///       Debug.Assert(s.CanWrite == false);
    ///    }
    /// }
    /// </code>
    /// </example>
    public System.IO.Stream GetStream(string localPath)
    {
      // GetSnapshotPath() returns a very funky-looking path.  The
      // System.IO methods can't handle these sorts of paths, so instead
      // we're using AlphaFS, another excellent library by Alpha Leonis.
      // Note that we have no 'using System.IO' at the top of the file.
      // (The Stream it returns, however, is just a System.IO stream.)
      return File.OpenRead(GetSnapshotPath(localPath));
    }

    /// <summary>
    /// The final phase of the backup involves some cleanup steps.
    /// If we're in component mode, we're supposed to notify each of the
    /// writers of the outcome of the backup.  Once that's done, or if
    /// we're not in component mode, we send the BackupComplete event to
    /// all of the writers.
    /// </summary>
    /// <param name="succeeded">Success value for all of the writers.</param>
    public void Complete(bool succeeded)
    {
      try
      {
        // The BackupComplete event must be sent to all of the writers.
        using (IVssAsync async = _backup.BackupComplete())
          async.Wait();
      }
      // Not sure why, but this throws a VSS_BAD_STATE on XP and W2K3.
      // Per some forum posts about this, I'm just ignoring it.
      catch (VssBadStateException) { }
    }

    /// <summary>
    /// This method takes the information in a file description and
    /// converts it to a full path specification - with wildcards.
    /// </summary>
    /// <remarks>
    /// Using the wildcard-to-regex library found at
    /// http://www.codeproject.com/KB/string/wildcmp.aspx on the
    /// output of this method might be very helpful.
    /// </remarks>
    /// <param name="file">Object describing a component's file.</param>
    /// <returns>
    /// Returns a full path, potentially including DOS wildcards.  Eg.
    /// 'c:\windows\config\*'.
    /// </returns>
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

      // Combine the file specification and the directory name.
      return Path.Combine(path, file.FileSpecification);
    }
  }
}
