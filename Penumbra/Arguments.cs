using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Penumbra
{
  class Arguments
  {
    private Arg[] _opt;
    public Arg[] Options
    {
      get
      {
        return _opt;
      }
      private set
      {
        _opt = value;
      }
    }
    private Arg[] _args;
    public Arg[] Args {
      get { return _args; }
      private set { _args = value; }
    }
    public string Prefix { get; private set; }
    public string Sep { get; private set; }

    public Arguments(bool has, params Arg[] options)
      : this(null)
    {
      if (has) Options = options;
    }
    public Arguments(params Arg[] args)
      : this("-", " ", null, args)
    { }
    public Arguments(string prefix, string separator, Arg[] options, Arg[] args)
    {
      if (String.IsNullOrEmpty(prefix) || String.IsNullOrEmpty(separator))
        throw new ArgumentException("Only non-empty strings are accepted.");

      if(prefix == separator)
        throw new ArgumentException("Prefix and separator must not be the same.");

      if ("" == prefix.Trim())
        throw new ArgumentException("Invalid prefix: whitespace is not allowed.");

      // Whitespace is already stripped from args, so it's not considered
      Prefix = prefix.Trim();
      Sep = separator.Trim(); 
      Options = options;
      Args = args;
    }

    public void ShowUsage()
    {
      bool[] has = {_opt != null && _opt.Length > 0, _args != null && _args.Length > 0};
      string exe = AppDomain.CurrentDomain.FriendlyName;
      string command = exe + " ";
      if (has[0])
      {
        foreach (Arg o in _opt)
        {
          string option = "<" + o.Name + ">";
          //if (!o.Required) option = "[" + option + "]";
          command += option + " ";
        }
      }
      if (has[1]) command += "[arguments]";
      Console.WriteLine("\n\t" + command );

      string s = (String.IsNullOrEmpty(Sep) ? " " : Sep);
      if (has[0]) // Options
      {
        Console.WriteLine("\n\tOPTIONS");
        foreach (Arg o in _opt)
          Console.WriteLine("\n\t\t" + o.Name + "\t" + o.Info);
      }

      if (has[1]) // Arguments
      {
        Console.WriteLine("\n\tARGUMENTS");
        foreach (Arg a in _args)
        {
          Console.WriteLine("\n\t\t" + Prefix + a.Name +"\t" + a.Info);
          if(a.DependsOn != null && a.DependsOn.Length > 0)
            Console.WriteLine("\t\t\tRequires: " + Prefix + String.Join(", " + Prefix, a.DependsOn) + ".");
        }
      }
      // Syntax
      Console.WriteLine(
        "\n\tSYNTAX"
        + "\n\n\t\t\"quotes around whitespace\""
        + "\n\n\t\t" + String.Format("{0}arg{1}value", Prefix, s)
        + "\n\n\t\texample:"
        + "\n\t\t\t" + String.Format(
          "{0}"
          + (has[0] ? " <options> " : String.Empty)
          + (has[1] ? " {1}foo{2}3 {1}bar{2}\"a space\"" : String.Empty),
          exe, Prefix, s)
      );
    }

    public Dictionary<string, string> Parse(string[] args)
    {
      Dictionary<string, string> argList = new Dictionary<string, string>(args.Length);
      Arg currentArg = null;
      bool parseOpt = true;
      // Extract values
      for(int i = 0; i < args.Length; i++)
      {
        // Single prefix means it's an arg, double prefix means it's a literal value
        if (args[i].StartsWith(Prefix) && !args[i].StartsWith(Prefix + Prefix))
        {
          parseOpt = false;
          try
          {
            currentArg = _args.First<Arg>(
              a => args[i].Substring(Prefix.Length).Equals(a.Name, StringComparison.OrdinalIgnoreCase)
            );
          }
          catch
          {
            throw new Exception("Unknown argument: " + args[i]);
          }

          if (!argList.ContainsKey(currentArg.Name)) argList[currentArg.Name] = "true";
          if (Sep == "") continue; // Parse the next token
        }
        
        // Token is an option
        if (parseOpt && Options != null && i < Options.Length)
        {
          argList[Options[i].Name] = args[i];
        }
        // Token is an argument with value
        else if (currentArg != null)
        {
          if(currentArg.HasValue)
          {
            // Extract the value
            if (Sep == "")
            {
              argList[currentArg.Name] = args[i];
            }
            else
            {
              argList[currentArg.Name] = args[i].Substring(args[i].IndexOf(Sep));
            }
          }
          currentArg = null;
        }
        else
        {
          throw new Exception("Unexpected command-line token: `" + args[i] + "`");
        }
      }
      // Verify dependencies
      foreach (Arg arg in _args)
      {
        bool hasArg = argList.ContainsKey(arg.Name);
        if (arg.Required && !hasArg)
        {
          throw new Exception(Prefix + arg.Name + " is a required argument.");
        }
        else if (
          hasArg
          && arg.DependsOn != null
          && arg.DependsOn.Length !=
          ( from a in argList.Keys
            from d in arg.DependsOn
            where d == a
            select a
          ).Count()
        ){
          string msg = Prefix + arg.Name + " is only allowed with the argument(s) " +
            Prefix + String.Join(", " + Prefix, arg.DependsOn) + ".";
          throw new Exception(msg);
        }
      }

      return argList;
    }
  }

  class Arg
  {
    public string Name { get; set; }
    public string Info { get; set; }
    public string[] DependsOn { get; set; }
    public bool Required { get; set; }
    public bool HasValue { get; set; }

    public Arg(string name, string info, bool required, bool hasvalue, params string[] deps)
    {
      Name = name;
      Info = info;
      Required = required;
      HasValue = hasvalue;
      DependsOn = deps;
    }
    public Arg(string name, string info, bool required, bool hasvalue)
      : this(name, info, required, hasvalue, null)
    { }
    public Arg(string name, string info, bool required)
      : this(name, info, required, false)
    { }
    public Arg(string name, string info)
      : this(name, info, false)
    { }
  }
}
