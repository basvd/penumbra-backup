using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Penumbra
{
  public class MetadataEntry : IDisposable
  {
    string _name;
    Stream _stream;

    public string Name
    {
      get
      {
        return _name;
      }
    }

    public Stream Stream
    {
      get
      {
        return _stream;
      }
    }

    // TODO: Allow strings instead of streams for simple metadata
    public MetadataEntry(string name, Stream s)
    {
      _name = name;
      _stream = s;
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
