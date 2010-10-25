using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using DrWPF.Windows.Data;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Collections.Specialized;
using System.IO;

namespace Penumbra
{
  // Holds configuration for a certain backup job
  public class Config : ICloneable, INotifyPropertyChanged
  {
    #region Deprecated
    //_cfg _clone;
    //public _cfg Undo
    //{
    //  get
    //  {
    //    return _clone;
    //  }
    //}

    //public void SaveClone()
    //{
    //  _clone = Clone();
    //}
    #endregion

    string _name;
    public string Name
    {
      get { return _name; }
      set { _name = value; NotifyPropertyChanged("Name"); }
    }

    string _target;
    public string Target
    {
      get
      {
        if (_target == null)
          _target = ParseTarget(Options["target"]);
        return _target;
      }
      set
      {
        Options["target"] = value;
        _target = ParseTarget(value);
        NotifyPropertyChanged("Target");
      }
    }

    public readonly ObservableDictionary<string, bool> Flags;
    public readonly ObservableDictionary<string, string> Options;
    public readonly ObservableCollection<string> Sources;
    public readonly ObservableCollection<string> Exclude;

    public Config()
    {
      // Defaults
      Name = "Default";

      // Flags
      Flags = new ObservableDictionary<string, bool>();
      Flags.Add("recursive", true);
      Flags.Add("incremental", false);
      Flags.Add("clear_archive_bit", false);

      // Options
      Options = new ObservableDictionary<string, string>();
      Options.Add("format", "zip");
      Options.Add("date_format", "dd-MM-yyyy");
      Options.Add("target", "backup_{date}.7z");

      // Lists
      Sources = new ObservableCollection<string>();
      Exclude = new ObservableCollection<string>();
    }

    public Config(JObject data) : this()
    {
      if (data["name"] != null)
        Name = (string) data["name"];

      // Options
      if (data["options"] != null)
      {
        Options = new ObservableDictionary<string, string>(MergeSettings<string>(Options,
          JsonConvert.DeserializeObject<Dictionary<string, string>>(data["options"].ToString())
        ));
      }

      // Flags
      if (data["flags"] != null)
      {
        Flags = new ObservableDictionary<string, bool>(MergeSettings<bool>(Flags,
          JsonConvert.DeserializeObject<Dictionary<string, bool>>(data["flags"].ToString())
        ));
      }

      // Sources
      if (data["sources"] != null)
      {
        foreach (JToken s in (JArray) data["sources"])
        {
          Sources.Add((string) s);
        }
      }

      // Exclude
      if (data["exclude"] != null)
      {
        foreach (JToken x in (JArray) data["exclude"])
        {
          Exclude.Add((string) x);
        }
      }
    }

    public object Clone()
    {
      Config clone = new Config();
      foreach (PropertyInfo p in this.GetType().GetProperties())
      {
        object v = p.GetValue(this, null);
        object copy = null;
        if (v.GetType().GetInterface("IDictionary")!= null)
        {
          IDictionary<object, object> d = v as IDictionary<object, object>;
          copy = Activator.CreateInstance(v.GetType());
          foreach (KeyValuePair<object, object> pair in d)
          {
            (copy as IDictionary<object, object>).Add(pair.Key, pair.Value);
          }
        }
        else if (v.GetType().GetInterface("ICollection") != null)
        {
          ICollection<object> c = v as ICollection<object>;
          copy = Activator.CreateInstance(v.GetType());
          foreach (object obj in c)
          {
            (copy as ICollection<object>).Add(obj);
          }
        }
        else if (v.GetType().GetInterface("ICloneable") != null)
        {
          copy = (v as ICloneable).Clone();
        }
        else
        {
          copy = v;
        }
        p.SetValue(clone, copy, null);
      }
      return clone;
    }

    // Replaces template tags in target
    // TODO: Let BackupEngine decide the filename
    string ParseTarget(string target)
    {
      if (target == null)
        return null;
      DateTime time = DateTime.Now;
      target += ".zip";
      return target
        .Replace("{name}", Name)
        .Replace("{date}", time.ToString(Options["date_format"]));
    }

    public string ToJson()
    {
      return ToJson(true);
    }

    public string ToJson(bool indent)
    {
      Dictionary<string, object> map = new Dictionary<string, object>(5);
      map.Add("name", Name);
      map.Add("options", Options);
      map.Add("flags", Flags);
      map.Add("sources", Sources);
      map.Add("exclude", Exclude);
      return JsonConvert.SerializeObject(map, indent ? Formatting.Indented : Formatting.None);
    }

    public void ToFile(string filename)
    {
      StreamWriter f = new StreamWriter(filename);
      f.Write(this.ToJson(true));
      f.Close();
    }

    #region Property changed
    public event PropertyChangedEventHandler PropertyChanged;
    private void NotifyPropertyChanged(String info)
    {
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(info));
      }
    }
    #endregion

    // Load job configuration
    public static List<Config> ReadFile(string filename)
    {
      if (System.IO.File.Exists(filename))
      {
        Console.WriteLine("Reading `" + filename + "`...");
        try
        {
          JObject data = JObject.Parse(
            "{ '_bkps' : " +
            System.IO.File.ReadAllText(filename) +
            " }");
          List<Config> cfgs = new List<Config>();
          if (data["_bkps"].Type == JTokenType.Array)
          {
            foreach (JObject j in data["_bkps"].Children<JObject>())
            {
              cfgs.Add(new Config(j));
            }
          }
          else if (data["_bkps"].Type == JTokenType.Object)
          {
            cfgs.Add(new Config((JObject) data["_bkps"]));
          }
          return cfgs;
        }
        catch (Exception e)
        {
          Console.WriteLine(e.Message);
          Console.WriteLine("An error occured while reading `" + filename + "`!");
          return null;
        }
      }
      else
      {
        Console.WriteLine("File `" + filename + "` does not exist!");
        return null;
      }
    }

    public static void WriteFile(List<Config> cfgs, string filename)
    {
      StreamWriter f = new StreamWriter(filename);
      if (cfgs.Count == 1)
        f.Write(JsonConvert.SerializeObject(cfgs[0], Formatting.Indented));
      else
        f.Write(JsonConvert.SerializeObject(cfgs, Formatting.Indented)); // JSON array?
      f.Close();
    }

    static Dictionary<string, TValue> MergeSettings<TValue>(IDictionary<string, TValue> defaultArgs, IDictionary<string, TValue> args)
    {
      return defaultArgs.Union(args)
          .ToLookup(pair => pair.Key, pair => pair.Value)
          .ToDictionary(pair => pair.Key, pair => pair.Last());
    }
  }
}
