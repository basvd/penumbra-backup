using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System.IO;
using System.Diagnostics;

namespace Penumbra
{
  class SubInAclExtension : IBackupExtension
  {
    // TODO: Read permissions from snapshot (if possible)

    public SubInAclExtension(Config c) : base(c)
    {
      if (!File.Exists("subinacl.exe")) throw new FileNotFoundException("subinacl.exe can not be found.");
    }

    override public MetadataEntry GetMetadata()
    {
      Console.WriteLine("Storing file permissions...");
      string args = "/noverbose /nostatistic /subdirectories {0}\\* /display";
      foreach (string x in _cfg.Exclude)
      {
        if (x.StartsWith("r/") && x.EndsWith("/")) continue;
        else if (x.StartsWith("x/") && x.EndsWith("/"))
        {
          string[] xs = x.Substring(2, x.Length - 3).Split(';');
          foreach (string s in xs)
          {
            args += " /objectexclude=*" + (s.StartsWith(".") ? s : '.' + s);
          }
        }
        else
        {
          args += " /pathexclude=\"" + x + '"';
        }
      }
      string output = "";
      foreach (string source in _cfg.Sources)
      {
        //if (!Directory.Exists(source)) continue;
        Process p = new Process();
        p.StartInfo.FileName = "subinacl.exe";
        p.StartInfo.Arguments = String.Format(args, '"' + source + '"');

        // Redirect output
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.StandardOutputEncoding = Encoding.Unicode;

        p.Start();
        output += p.StandardOutput.ReadToEnd();
        p.WaitForExit();
      }
      return new MetadataEntry("permissions", new MemoryStream(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, Encoding.Unicode.GetBytes(output))));
    }

    public override void OnWrite(BackupEntry entry) { }
  }
}
