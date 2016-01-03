﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using XPloit.Core.Attributes;
using XPloit.Core.Collections;
using XPloit.Core.Command;
using XPloit.Core.Command.Interfaces;
using XPloit.Core.Enums;
using XPloit.Core.Helpers;
using XPloit.Core.Interfaces;
using XPloit.Core.Requirements.Payloads;
using XPloit.Res;

namespace XPloit.Core.Listeners
{
    public class CommandListener : IListener, IAutoCompleteSource
    {
        ICommandLayer _IO = null;
        CommandMenu _Command = null;
        Module _CurrentGlobal = null;
        Module _Current = null;
        bool _IsStarted;

        #region IAutoCompleteSource
        public IEnumerable<string> GetCommand()
        {
            foreach (CommandMenuItem ci in _Command)
                foreach (string s in ci.Selector)
                    yield return s;
        }
        public IEnumerable<string> GetArgument(string command, string[] arguments)
        {
            switch (command.ToLowerInvariant().Trim())
            {
                case "man":
                case "help":
                    {
                        foreach (CommandMenuItem m in _Command)
                            foreach (string sep in m.Selector)
                                yield return sep;

                        break;
                    }
                case "use":
                    {
                        foreach (Module e in ModuleCollection.Current) yield return e.FullPath;
                        break;
                    }
                case "show":
                    {
                        if (arguments == null || arguments.Length <= 1)
                        {
                            foreach (string e in new string[] { "options", "config", "payloads", "targets" }) yield return e;
                        }
                        break;
                    }
                case "set":
                case "gset":
                    {
                        if (_Current == null) break;

                        if (arguments == null || arguments.Length <= 1)
                        {
                            Target[] t = _Current.Targets;
                            if (t != null && t.Length > 1) yield return "Target";
                            if (_Current.PayloadRequirements != null)
                                yield return "Payload";

                            if (_Current.Payload != null)
                                foreach (PropertyInfo pi in ReflectionHelper.GetProperties(_Current.Payload, true, true, true)) yield return pi.Name;

                            foreach (PropertyInfo pi in ReflectionHelper.GetProperties(_Current, true, true, true)) yield return pi.Name;
                        }
                        else
                        {
                            string pname = arguments[0].ToLowerInvariant().Trim();
                            switch (pname)
                            {
                                case "target":
                                    {
                                        Target[] ts = _Current.Targets;
                                        if (ts != null)
                                        {
                                            foreach (Target t in ts)
                                                yield return t.Id.ToString();
                                        }
                                        break;
                                    }
                                case "payload":
                                    {
                                        IPayloadRequirements req = _Current.PayloadRequirements;
                                        if (req != null)
                                        {
                                            foreach (Payload p in PayloadCollection.Current)
                                            {
                                                if (!req.IsAllowed(p)) continue;
                                                yield return p.FullPath;
                                            }
                                        }
                                        break;
                                    }
                                default:
                                    {
                                        // By property value
                                        PropertyInfo[] pi = null;

                                        if (_Current.Payload != null) pi = ReflectionHelper.GetProperties(_Current.Payload, pname);
                                        if (pi == null || pi.Length == 0) pi = ReflectionHelper.GetProperties(_Current, pname);
                                        if (pi != null && pi.Length > 0)
                                        {
                                            if (pi[0].PropertyType == typeof(bool))
                                            {
                                                yield return "true";
                                                yield return "false";
                                            }
                                            else
                                            {
                                                if (pi[0].PropertyType.IsEnum)
                                                {
                                                    foreach (string name in Enum.GetNames(pi[0].PropertyType))
                                                        yield return name;
                                                }
                                            }
                                        }
                                        break;
                                    }
                            }
                        }
                        break;
                    }
            }
        }
        public StringComparison ComparisonMethod { get { return StringComparison.InvariantCultureIgnoreCase; } }
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public CommandListener(ICommandLayer command)
        {
            CommandMenu cmd = new CommandMenu(command, this);
            _IO = command;

            cmd.Add(new string[] { "banner" }, cmdBanner, Lang.Get("Man_Banner"));
            cmd.Add(new string[] { "version" }, cmdVersion, Lang.Get("Man_Version"));
            cmd.Add(new string[] { "clear", "cls" }, cmdClear, Lang.Get("Man_Clear"));
            cmd.Add(new string[] { "cd", "cd..", "cd\\", "cd/", "back" }, cmdCD, Lang.Get("Man_Cd"));

            // Modules command
            cmd.Add(new string[] { "use" }, cmdUse, Lang.Get("Man_Use"));
            cmd.Add(new string[] { "show" }, cmdShow, Lang.Get("Man_Show"));
            cmd.Add(new string[] { "set" }, cmdSet, Lang.Get("Man_Set"));
            cmd.Add(new string[] { "gset" }, cmdSetG, Lang.Get("Man_Set_Global"));
            cmd.Add(new string[] { "check" }, cmdCheck, Lang.Get("Man_Check"));
            cmd.Add(new string[] { "exploit", "run" }, cmdRun, Lang.Get("Man_Run"));
            cmd.Add(new string[] { "reload" }, cmdReload, Lang.Get("Man_Reload"));
            cmd.Add(new string[] { "resource" }, cmdResource, Lang.Get("Man_Resource"));
            cmd.Add(new string[] { "kill" }, cmdKill, Lang.Get("Man_Kill"));
            cmd.Add(new string[] { "jobs" }, cmdJobs, Lang.Get("Man_Jobs"));
            cmd.Add(new string[] { "load" }, cmdLoad, Lang.Get("Man_Load"));

            //_Command.Add(new string[] { "search" }, null, Lang.Get("Man_Search"));
            //_Command.Add(new string[] { "info" }, null, Lang.Get("Man_Info"));

            _Command = cmd;
        }

        bool CheckModule(bool checkRequieredProperties)
        {
            if (_Current == null)
            {
                WriteError(Lang.Get("Require_Module"));
                return false;
            }

            string propertyName;
            if (checkRequieredProperties && !_Current.CheckRequiredProperties(out propertyName))
            {
                _Current.WriteInfo(Lang.Get("Require_Set_Property", propertyName));
                return false;
            }
            return true;
        }

        #region Commands
        public void cmdClear(string args) { _IO.Clear(); }
        public void cmdCD(string args)
        {
            _Current = null;
            _Command.PromptCharacter = "> ";
        }
        public void cmdJobs(string args)
        {
            if (JobCollection.Current.Count <= 0)
            {
                WriteInfo(Lang.Get("Nothing_To_Show"));
                return;
            }

            CommandTable tb = new CommandTable();

            _IO.WriteLine("");

            tb.AddRow(tb.AddRow(Lang.Get("Id"), Lang.Get("Status"), Lang.Get("Module")).MakeSeparator());

            foreach (Job j in JobCollection.Current)
            {
                CommandTableRow row;
                if (j.IsRunning)
                {
                    row = tb.AddRow(j.Id.ToString(), Lang.Get("Running"), j.FullPathModule);
                    row[0].Align = CommandTableCol.EAlign.Right;
                    row[1].ForeColor = ConsoleColor.Green;
                }
                else
                {
                    row = tb.AddRow(j.Id.ToString(), Lang.Get("Dead"), j.FullPathModule);
                    row[0].Align = CommandTableCol.EAlign.Right;
                    row[1].ForeColor = ConsoleColor.Red;
                }
            }

            tb.OutputColored(_IO);
            _IO.WriteLine("");
        }
        public void cmdVersion(string args)
        {
            _IO.WriteLine("");

            CommandTable tb = new CommandTable();

            _IO.WriteLine(Lang.Get("Version_Start"));
            _IO.WriteLine("");

            tb.AddRow(tb.AddRow(Lang.Get("File"), Lang.Get("Version")).MakeSeparator());

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GlobalAssemblyCache) continue;

                string file = "??";
                if (string.IsNullOrEmpty(asm.Location))
                    file = asm.ManifestModule.ScopeName;
                else file = Path.GetFileName(asm.Location);

                tb.AddRow(file, asm.ImageRuntimeVersion);
            }

            tb.AddRow("", "");

            tb.AddRow(tb.AddRow(Lang.Get("Module"), Lang.Get("Count")).MakeSeparator());
            tb.AddRow(Lang.Get("Modules"), ModuleCollection.Current.Count.ToString());
            tb.AddRow(Lang.Get("Encoders"), EncoderCollection.Current.Count.ToString());
            tb.AddRow(Lang.Get("Payloads"), PayloadCollection.Current.Count.ToString());
            tb.AddRow(Lang.Get("Nops"), NopCollection.Current.Count.ToString());

            _IO.WriteLine(tb.Output());
        }
        public void cmdBanner(string args)
        {
            _IO.WriteLine("");
            BannerHelper.GetRandomBanner(_IO);
            _IO.WriteLine("");
        }
        public void cmdCheck(string args)
        {
            if (!CheckModule(true)) return;
            args = args.Trim();

            try
            {
                switch (_Current.Check())
                {
                    case ECheck.CantCheck: _Current.WriteInfo(Lang.Get("Check_CantCheck")); break;
                    case ECheck.Error: _Current.WriteInfo(Lang.Get("Check_Result"), Lang.Get("Error"), ConsoleColor.Red); break;
                    case ECheck.NotSure: _Current.WriteInfo(Lang.Get("Check_NotSure")); break;
                    case ECheck.Ok: _Current.WriteInfo(Lang.Get("Check_Result"), Lang.Get("Ok"), ConsoleColor.Green); break;
                }
            }
            catch (Exception e)
            {
                _Current.WriteError(e.Message);
            }
        }
        public void cmdRun(string args)
        {
            if (!CheckModule(true)) return;
            args = args.Trim();

            try
            {
                if (!_Current.Run())
                    _Current.WriteError(Lang.Get("Run_Error"));
            }
            catch (Exception e)
            {
                _Current.WriteError(e.Message);
            }
        }
        public void cmdKill(string args)
        {
            args = args.Trim();

            try
            {
                if (string.IsNullOrEmpty(args))
                {
                    WriteError(Lang.Get("Incorrect_Command_Usage"));
                    return;
                }

                int job = (int)ConvertHelper.ConvertTo(args, typeof(int));
                if (JobCollection.Current.Kill(job))
                {
                    WriteInfo(Lang.Get("Kill_Job"), Lang.Get("Ok"), ConsoleColor.Green);
                }
                else
                {
                    WriteInfo(Lang.Get("Kill_Job"), Lang.Get("Error"), ConsoleColor.Red);
                }
            }
            catch (Exception e)
            {
                WriteError(e.Message);
            }
        }
        public void cmdReload(string args)
        {
            // Todo comentar en Res.res los comandos y acabar del set para abajo
            if (!CheckModule(false)) return;

            _IO.AddInput("use " + _Current.FullPath);
            WriteInfo(Lang.Get("Reloaded_Module", _Current.FullPath), Lang.Get("Ok"), ConsoleColor.Green);
        }
        public void cmdLoad(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                WriteError(Lang.Get("Incorrect_Command_Usage"));
                return;
            }
            args = args.Trim();
            if (!File.Exists(args))
            {
                WriteError(Lang.Get("File_Not_Exists", args));
                return;
            }

            try
            {
                _IO.SetForeColor(ConsoleColor.Gray);
                _IO.Write(Lang.Get("Reading_File", args));

                Assembly.Load(File.ReadAllBytes(args));
                //Assembly.LoadFile(args);

                _IO.SetForeColor(ConsoleColor.Green);
                _IO.WriteLine(Lang.Get("Ok").ToUpperInvariant());
            }
            catch
            {
                _IO.SetForeColor(ConsoleColor.Red);
                _IO.WriteLine(Lang.Get("Error").ToUpperInvariant());
            }
            CommandTable tb = new CommandTable();

            _IO.WriteLine("");

            tb.AddRow(tb.AddRow(Lang.Get("Type"), Lang.Get("Count")).MakeSeparator());

            tb.AddRow(Lang.Get("Modules"), ModuleCollection.Current.Load().ToString())[1].Align = CommandTableCol.EAlign.Right;
            tb.AddRow(Lang.Get("Payloads"), PayloadCollection.Current.Load().ToString())[1].Align = CommandTableCol.EAlign.Right;
            tb.AddRow(Lang.Get("Encoders"), EncoderCollection.Current.Load().ToString())[1].Align = CommandTableCol.EAlign.Right;
            tb.AddRow(Lang.Get("Nops"), NopCollection.Current.Load().ToString())[1].Align = CommandTableCol.EAlign.Right;

            tb.OutputColored(_IO);
            _IO.WriteLine("");
        }
        public void cmdResource(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                WriteError(Lang.Get("Incorrect_Command_Usage"));
                return;
            }
            args = args.Trim();
            if (!File.Exists(args))
            {
                WriteError(Lang.Get("File_Not_Exists", args));
                return;
            }
            try
            {
                _IO.SetForeColor(ConsoleColor.Gray);
                _IO.Write(Lang.Get("Reading_File", args));

                foreach (string line in File.ReadAllLines(args))
                {
                    string ap = line.Trim();
                    if (string.IsNullOrEmpty(ap)) continue;
                    _IO.AddInput(ap);
                }

                _IO.SetForeColor(ConsoleColor.Green);
                _IO.WriteLine(Lang.Get("Ok").ToUpperInvariant());
            }
            catch
            {
                _IO.SetForeColor(ConsoleColor.Red);
                _IO.WriteLine(Lang.Get("Error").ToUpperInvariant());
            }
        }
        public void cmdSet(string args) { cmdSet(args, false); }
        public void cmdSetG(string args) { cmdSet(args, true); }
        public void cmdSet(string args, bool global)
        {
            if (!CheckModule(false)) return;
            args = args.Trim();

            string[] prop = ArgumentHelper.ArrayFromCommandLine(args);
            if (prop == null || prop.Length != 2)
            {
                WriteError(Lang.Get("Incorrect_Command_Usage"));
                return;
            }

            if (!_Current.SetProperty(prop[0], prop[1]))
            {
                WriteError(Lang.Get("Error_Converting_Value"));
            }
            else
            {
                if (global) _CurrentGlobal.SetProperty(prop[0], prop[1]);
            }
        }
        public void cmdShow(string args)
        {
            // Todo comentar en Res.res los comandos y acabar del set para abajo
            if (!CheckModule(false)) return;
            args = args.Trim().ToLowerInvariant();

            switch (args)
            {
                case "":
                case "options":
                case "config":
                    {
                        // set target id
                        Target[] ps = _Current.Targets;
                        if (ps != null)
                        {
                            int ix = 0;
                            foreach (Target t in ps) { t.Id = ix; ix++; }
                        }

                        CommandTable tb = new CommandTable();

                        tb.AddRow(Lang.Get("Path"), _Current.Path, "");
                        tb.AddRow(Lang.Get("Name"), _Current.Name, "");

                        tb.AddRow("", "", "");
                        tb.AddSeparator(3, '*');
                        tb.AddRow("", "", "");

                        if (!string.IsNullOrEmpty(_Current.Author))
                        {
                            CommandTableRow row = tb.AddRow(1, Lang.Get("Author"), _Current.Author, "");
                            row[1].Align = CommandTableCol.EAlign.None;
                            row[2].Align = CommandTableCol.EAlign.None;
                        }
                        if (!string.IsNullOrEmpty(_Current.Description))
                        {
                            foreach (CommandTableRow row in tb.AddSplitRow(1, Lang.Get("Description"), _Current.Description, ""))
                            {
                                row[1].Align = CommandTableCol.EAlign.None;
                                row[2].Align = CommandTableCol.EAlign.None;
                            }
                        }
                        if (_Current.DisclosureDate != DateTime.MinValue) tb.AddRow(Lang.Get("DisclosureDate"), _Current.DisclosureDate.ToString(), "");
                        tb.AddRow(Lang.Get("IsLocal"), _Current.IsLocal.ToString(), "");
                        tb.AddRow(Lang.Get("IsRemote"), _Current.IsRemote.ToString(), "");

                        if (_Current.References != null && _Current.References.Length > 0)
                        {
                            tb.AddRow("", "", "");

                            StringBuilder sb = new StringBuilder();
                            foreach (Reference r in _Current.References)
                            {
                                if (r == null) continue;
                                sb.AppendLine(r.Type.ToString() + " - " + r.Value);
                            }

                            foreach (CommandTableRow row in tb.AddSplitRow(1, Lang.Get("References"), sb.ToString(), ""))
                            {
                                row[1].Align = CommandTableCol.EAlign.None;
                                row[2].Align = CommandTableCol.EAlign.None;
                            }
                        }

                        bool fistAll = true;
                        bool hasX0 = false;
                        bool hasX2 = false;
                        for (int x = 0; x <= 3; x++)
                        {
                            PropertyInfo[] pis = null;

                            object pv = _Current;
                            switch (x)
                            {
                                case 0:
                                    {
                                        if (_Current.PayloadRequirements != null)
                                        {
                                            pis = ReflectionHelper.GetProperties(_Current, "Payload");
                                            hasX0 = pis != null && pis.Length > 0;
                                        }
                                        break;
                                    }
                                case 1:
                                    {
                                        Target[] t = _Current.Targets;
                                        if (t != null && t.Length > 1)
                                            pis = ReflectionHelper.GetProperties(_Current, "Target");
                                        break;
                                    }
                                case 2:
                                    {
                                        pis = ReflectionHelper.GetProperties(_Current, true, true, true);
                                        hasX2 = pis != null && pis.Length > 0;
                                        break;
                                    }
                                case 3:
                                    {
                                        pv = _Current.Payload;
                                        if (_Current.Payload == null) pis = null;
                                        else pis = ReflectionHelper.GetProperties(_Current.Payload, true, true, true);
                                        break;
                                    }
                            }

                            if (pis != null)
                            {
                                bool primera = (x != 1 || !hasX0);
                                foreach (PropertyInfo pi in pis)
                                {
                                    ConfigurableProperty c = pi.GetCustomAttribute<ConfigurableProperty>();
                                    if (c == null)
                                        continue;

                                    if (primera)
                                    {
                                        if (x == 3 && hasX2)
                                        {
                                            tb.AddRow("", "", "");
                                        }
                                        else
                                        {
                                            tb.AddRow("", "", "");
                                            tb.AddSeparator(3, fistAll ? '*' : '.');
                                            tb.AddRow("", "", "");
                                        }
                                        primera = false;
                                        fistAll = false;
                                    }

                                    object val = pi.GetValue(pv);
                                    if (val == null)
                                    {
                                        val = "NULL";
                                        CommandTableRow row = tb.AddRow(pi.Name, val.ToString(), c.Description);
                                        if (c.Required) row[1].ForeColor = ConsoleColor.Red;
                                        else
                                        {
                                            if (x == 0 || x == 3)
                                                row[1].ForeColor = ConsoleColor.Cyan;
                                        }
                                    }
                                    else
                                    {
                                        CommandTableRow row = tb.AddRow(pi.Name, val.ToString(), c.Description);
                                        if (x == 0 || x == 3)
                                            row[1].ForeColor = ConsoleColor.Cyan;
                                    }
                                }
                            }
                        }

                        string separator = tb.Separator;
                        foreach (CommandTableRow row in tb)
                        {
                            foreach (CommandTableCol col in row)
                            {
                                if (col.ReplicatedChar == '\0')
                                {
                                    switch (col.Index)
                                    {
                                        case 0: _IO.SetForeColor(ConsoleColor.DarkGray); break;
                                        case 1: _IO.SetForeColor(col.ForeColor); break;
                                        case 2: _IO.SetForeColor(ConsoleColor.Yellow); break;
                                    }
                                }
                                else _IO.SetForeColor(col.ForeColor);

                                if (col.Index != 0) _IO.Write(separator);
                                _IO.Write(col.GetFormatedValue());
                            }
                            _IO.WriteLine("");
                        }
                        break;
                    }
                case "payloads":
                    {
                        Payload[] ps = PayloadCollection.Current.GetPayloadAvailables(_Current.PayloadRequirements);
                        if (ps == null || ps.Length == 0)
                            WriteInfo(Lang.Get("Nothing_To_Show"));
                        else
                        {
                            CommandTable tb = new CommandTable();

                            tb.AddRow(tb.AddRow(Lang.Get("Name"), Lang.Get("Description")).MakeSeparator());

                            foreach (Payload p in ps)
                                tb.AddRow(p.FullPath, p.Description);

                            _IO.Write(tb.Output());
                        }
                        break;
                    }
                case "targets":
                    {
                        Target[] ps = _Current.Targets;
                        if (ps == null || ps.Length <= 1)
                            WriteInfo(Lang.Get("Nothing_To_Show"));
                        else
                        {
                            CommandTable tb = new CommandTable();

                            tb.AddRow(tb.AddRow(Lang.Get("Name"), Lang.Get("Description")).MakeSeparator());

                            int ix = 0;
                            foreach (Target p in ps)
                            {
                                p.Id = ix; ix++;
                                tb.AddRow(p.Id.ToString(), p.Name);
                            }

                            _IO.Write(tb.Output());
                        }
                        break;
                    }
                default:
                    {
                        // incorrect use
                        WriteError(Lang.Get("Incorrect_Command_Usage"));
                        _IO.AddInput("help show");
                        break;
                    }
            }
        }
        public void cmdUse(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                _Current = null;
                args = "";
            }
            else
            {
                args = args.Trim();
                _CurrentGlobal = ModuleCollection.Current.GetByFullPath(args, false);
                if (_CurrentGlobal != null)
                {
                    _Current = (Module)ReflectionHelper.Clone(_CurrentGlobal, true);
                    _Current.Prepare(_IO);
                }
                else
                {
                    _Current = null;
                }
            }

            if (_Current == null) WriteError(Lang.Get(string.IsNullOrEmpty(args) ? "Command_Incomplete" : "Module_Not_Found", args));
            else
            {
                _Command.PromptCharacter = _Current.Name + "> ";
            }
        }
        #endregion

        #region Log methods
        void WriteStart(string ch, ConsoleColor color)
        {
            if (_IO == null) return;

            _IO.SetForeColor(ConsoleColor.Gray);
            _IO.Write("[");
            _IO.SetForeColor(color);
            _IO.Write(ch);
            _IO.SetForeColor(ConsoleColor.Gray);
            _IO.Write("] ");

        }
        public void WriteError(string error)
        {
            if (_IO == null) return;

            if (string.IsNullOrEmpty(error)) error = "";
            else error = error.Trim();

            WriteStart("!", ConsoleColor.Red);
            _IO.SetForeColor(ConsoleColor.Red);
            _IO.WriteLine(error.Replace("\n", "\n    "));
        }
        public void WriteInfo(string info)
        {
            if (_IO == null) return;

            if (string.IsNullOrEmpty(info)) info = "";
            else info = info.Trim();

            WriteStart("*", ConsoleColor.Cyan);
            _IO.WriteLine(info.Replace("\n", "\n    "));
        }
        public void WriteInfo(string info, string colorText, ConsoleColor color)
        {
            if (_IO == null) return;

            if (string.IsNullOrEmpty(info)) info = "";
            else info = info.Trim();

            WriteStart("*", ConsoleColor.Cyan);
            _IO.Write(info);

            if (!string.IsNullOrEmpty(colorText))
            {
                _IO.Write(" ... [");
                _IO.SetForeColor(color);
                _IO.Write(colorText);
                _IO.SetForeColor(ConsoleColor.Gray);
                _IO.WriteLine("]");
            }
        }
        #endregion

        public override bool IsStarted { get { return _IsStarted; } }
        public override bool Start()
        {
            _IsStarted = true;
            _Command.Run();
            return _IsStarted;
        }
        public override bool Stop()
        {
            _Command.Quit();
            _IsStarted = false;
            return true;
        }
    }
}