using System;
using System.Collections.Generic;

namespace Penumbra
{
  class Program
  {
    static void Main(string[] rawArgs)
    {
      Arg[] opts =
      {
        new Arg("action", "which action to perform   (backup|restore)"),
        new Arg("job", "backup job configuration file")
      };

      Arg[] args =
      {
        new Arg("help", "show usage information"),
        new Arg("i", "ask for confirmation")
        //new Arg("diff", "make differential backup", false, false, "action", "help")
      };
      Arguments parser = new Arguments(
        "/",
        ":",
        opts, args);

      Dictionary<string, string> pArgs;

      try
      {
        pArgs = parser.Parse(rawArgs);
      }
      catch (Exception e)
      {
        Console.WriteLine(e.Message);
        Console.WriteLine("\nTry `" + AppDomain.CurrentDomain.FriendlyName + " /help` for usage information.");
        return;
      }

      if (pArgs.ContainsKey("help"))
      {
        parser.ShowUsage();
        return;
      }

      if (pArgs.ContainsKey("action") && pArgs["action"] == "backup" && pArgs.ContainsKey("job"))
      {
        List<Config> cfgs = Config.ReadFile(pArgs["job"]);
        try
        {
          if(cfgs.Count > 1) Console.WriteLine("`"+pArgs["job"]+"` contains multiple backup jobs.");
          foreach (Config cfg in cfgs)
          {
            BackupJob job = new BackupJob(cfg);
            if (!pArgs.ContainsKey("i"))
            {
              job.Run();
            }
            else
            {
              string yesNo = String.Empty;
              while (yesNo != "y" && yesNo != "n")
              {
                Console.Write("Start the backup? (y/n): ");
                yesNo = Console.ReadKey().KeyChar.ToString().ToLower();
                Console.WriteLine();
                if (yesNo == "y")
                  job.Run();
                else if (yesNo == "n")
                  Console.WriteLine("Backup cancelled.");
              }
            }
          }
        }
        catch (Exception e)
        {
          Console.WriteLine(e.Message);
        }
        //Console.WriteLine(filename.ToJson());
      }
      else
      {
        Console.WriteLine("Invalid usage.");
        Console.WriteLine("\nTry `" + AppDomain.CurrentDomain.FriendlyName + " /help` for usage information.");
        return;
      }
    }
  }
}
