using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Penumbra
{
  abstract class BackupEngine
  {
    bool _started;
    protected Config _cfg;

    public BackupEngine(Config c)
    {
      _started = false;
      _cfg = c;
    }

    public virtual void Init()
    {
      if (!_started) _started = true;
      else throw new InvalidOperationException("Backup engine has already been started.");
    }

    public virtual void Write(BackupEntry entry)
    {
      Write();
    }

    public virtual void Write(MetadataEntry meta)
    {
      Write();
    }

    protected void Write()
    {
      if (!_started) throw new InvalidOperationException("The backup engine is not ready.");
    }

    public virtual void Post()
    {
      if (!_started) throw new InvalidOperationException("The backup engine is not ready.");
      _started = false;
    }
  }
}
