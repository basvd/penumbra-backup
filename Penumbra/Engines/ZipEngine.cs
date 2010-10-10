using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using System.IO;

namespace Penumbra
{
  class ZipEngine : BackupEngine
  {
    byte[] _buffer;
    ZipOutputStream _zip;
    ZipEntryFactory _zipFactory;

    public ZipEngine(Config c) : base(c) { }

    override public void Init()
    {
      base.Init();

      _buffer = new byte[4096];
      _zipFactory = new ZipEntryFactory();
      _zip = new ZipOutputStream(File.Create(_cfg.Target));
      _zip.UseZip64 = UseZip64.Dynamic;
      //_zip.SetLevel(8);
      _zip.SetComment(_cfg.Flags["incremental"] ? "Incremental backup" : "Full backup");
    }

    override public void Write(BackupEntry entry)
    {
      base.Write();

      Console.WriteLine("Compressing: " + entry.RealPath);
      ZipEntry ez;
      string entryPath = Path.GetPathRoot(entry.RealPath).Remove(1) + "/";
      
      if (entry.Info.IsDirectory)
      {
        entryPath += _zipFactory.NameTransform.TransformDirectory(entry.RealPath);
        ez = _zipFactory.MakeDirectoryEntry(entryPath, false);
      }
      else if (entry.Info.IsFile)
      {
        // Windows specific metadata (timestamps)
        NTTaggedData meta = new NTTaggedData();
        meta.CreateTime = entry.Info.Created;
        meta.LastModificationTime = entry.Info.LastModified;
        meta.LastAccessTime = entry.Info.LastAccessed;

        entryPath += _zipFactory.NameTransform.TransformFile(entry.RealPath);
        ez = _zipFactory.MakeFileEntry(entryPath, false);
        ez.DateTime = entry.Info.LastModified;
        ez.ExtraData = meta.GetData();
      }
      else return;

      _zip.PutNextEntry(ez);

      if (entry.Info.IsFile)
      {
        using (Stream fs = entry.Stream)
        {
          int sourceBytes;
          do
          {
            sourceBytes = fs.Read(_buffer, 0, _buffer.Length);
            _zip.Write(_buffer, 0, sourceBytes);
          } while (sourceBytes > 0);
        }
      }
    }

    override public void Write(MetadataEntry entry)
    {
      ZipEntry ez = _zipFactory.MakeFileEntry("metadata/" + entry.Name, false);

      _zip.PutNextEntry(ez);

      using (Stream fs = entry.Stream)
      {
        int sourceBytes;
        do
        {
          sourceBytes = fs.Read(_buffer, 0, _buffer.Length);
          _zip.Write(_buffer, 0, sourceBytes);
        } while (sourceBytes > 0);
      }
    }

    override public bool Post()
    {
      base.Post();

      _zip.Close();
      _zip.Dispose();
      return true;
    }

  }
}
