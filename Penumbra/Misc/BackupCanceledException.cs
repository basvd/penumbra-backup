using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Penumbra.Misc
{
  class BackupCanceledException : Exception
  {
      public BackupCanceledException()
        : base()
      { }
      public BackupCanceledException(string message)
        : base(message)
      { }
      public BackupCanceledException(string message, Exception inner)
        : base(message, inner)
      { }

      // This constructor is needed for serialization.
      protected BackupCanceledException(SerializationInfo info, StreamingContext context)
        : base(info, context)
      { }
  }
}
