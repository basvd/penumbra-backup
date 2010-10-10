using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Penumbra
{
  public class ConsoleLog : AbstractLog
  {
    int _i = 0;
    char[] _spinner = new char[] { '|', '/', '-', '\\' };
    int _w = Console.WindowWidth - 13;

    override public void Write(string msg, Level level)
    {
      Console.WriteLine(Format(msg));
    }

    override public void Write(Exception exc, Level level)
    {
      Write(exc.Message, level);
    }

    override public void WriteProgress(int p, int total, Level level)
    {
      if (p == 0) Console.Write("\n");

      double a = Math.Round((double) p / total * 100.00, 2);
      Console.WriteLine(_spinner[_i++] + " " + (a + "%").PadRight(8));
      Console.SetCursorPosition(0, Console.CursorTop - 1);

      if (_i >= _spinner.Length) _i = 0;
    }
  }
}
