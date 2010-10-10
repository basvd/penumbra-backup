using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Alphaleonis.Win32.Filesystem;

namespace Penumbra
{
  public class IncrementalBackupFilter : FullBackupFilter
  {
    public IncrementalBackupFilter(Config c) : base(c) { }

    override public bool IsFileExcluded(BackupEntry entry)
    {
      // Exclude if archive attribute is not set (or entry is filtered)
      // Log
      if ((entry.Info.Attributes & FileAttributes.Archive) == FileAttributes.None)
      {
        Console.WriteLine(
          "File excluded: " + entry.RealPath
        );
        return true;
      }
      else
        return base.IsFileExcluded(entry);
    }
  }
}
