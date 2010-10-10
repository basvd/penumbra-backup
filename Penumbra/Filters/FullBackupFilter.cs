using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Penumbra
{
  public class FullBackupFilter : ExclusionFilter
  {
    public FullBackupFilter(Config c) : base(c) { }

    override public bool IsDirExcluded(BackupEntry entry)
    {
      return false;
    }

    override public bool IsFileExcluded(BackupEntry entry)
    {
      bool exclude = Config.Exclude.Any(
        x =>
        {
          if (x.StartsWith("r/") && x.EndsWith("/"))
          {
            // Regex
            return Regex.IsMatch(entry.RealPath, x.Substring(2, x.Length - 3));
          }
          else if (x.StartsWith("x/") && x.EndsWith("/"))
          {
            // File extension(s)
            return x.Substring(2, x.Length - 3).Split(';')
              .Any(
                y =>
                {
                  return (y.Length > 0 && entry.RealPath.EndsWith("." + y, StringComparison.OrdinalIgnoreCase))
                  || (y.StartsWith(".") && entry.RealPath.EndsWith(y, StringComparison.OrdinalIgnoreCase));
                }
              );
          }
          else
          {
            // Basic path
            return entry.RealPath.StartsWith(x);
          }
        }
      );
      // Log
      if(exclude)
        Console.WriteLine(
          "File excluded: " + entry.RealPath
        );
      return exclude;
    }
  }
}
