using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Penumbra
{
  public abstract class ExclusionFilter
  {
    Config _cfg;

    public Config Config
    {
      get
      {
        return _cfg;
      }
    }

    public ExclusionFilter(Config c)
    {
      _cfg = c;
    }

    public abstract bool IsDirExcluded(BackupEntry entry);
    public abstract bool IsFileExcluded(BackupEntry entry);
  }
}
