using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Penumbra
{
  public abstract class AbstractLog
  {
    public enum Level
    {
      Debug = 0,
      Info = 1,
      Warning = 2,
      Error = 3
    }

    public abstract void Write(string msg, Level level);
    public abstract void Write(Exception exc, Level level);
    public abstract void WriteProgress(int p, int total, Level level);

    protected string Format(string msg)
    {
      return String.Format("[{0:s}] {1}", DateTime.Now, msg);
    }
  }
}
