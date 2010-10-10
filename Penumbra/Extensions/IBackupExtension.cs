using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Penumbra
{
  // TODO: Event based API?
  public abstract class IBackupExtension
  {
    protected Config _cfg;

    public IBackupExtension(Config c)
    {
      _cfg = c;
    }

    public abstract MetadataEntry GetMetadata();
    public abstract void OnWrite(BackupEntry entry);
  }
}
