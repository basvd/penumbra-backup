using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Alphaleonis.Win32.Filesystem;

namespace Penumbra
{
  public class BackupEntry : IDisposable
  {
    FileSystemInfo _info;

    ShadowCopy _shadow;
    System.IO.Stream _stream;

    public string RealPath
    {
      get
      {
        return _shadow.GetRealPath(SnapshotPath);
      }
    }

    public string SnapshotPath
    {
      get
      {
        return _info.FullName;
      }
    }

    public System.IO.Stream Stream
    {
      get
      {
        if (_stream == null)
          _stream = File.OpenRead(SnapshotPath);
        return _stream;
      }
    }

    public FileSystemEntryInfo Info
    {
      get
      {
        return File.GetFileSystemEntryInfo(SnapshotPath);
      }
    }

    public bool CanRead
    {
      get
      {
        try
        {
          return Stream.CanRead;
        }
        catch (Exception)
        {
          return false;
        }
      }
    }

    public BackupEntry(string path, ShadowCopy vss)
    {
      _info = new FileInfo(path);
      _shadow = vss;
    }

    public void Dispose()
    {
      if (_stream != null)
      {
        _stream.Close();
        _stream.Dispose();
        _stream = null;
      }
    }
  }
}
