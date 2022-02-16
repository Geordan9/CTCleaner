using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;
using CTCleaner.Util.Extension;
using GCLILib.Core;
using GCLILib.Util;
using static GCLILib.Util.ConsoleTools;

namespace CTCleaner;

internal class Program
{
    private static readonly ConsoleOption[] ConsoleOptions =
    {
        new()
        {
            Name = "Repair",
            ShortOp = "-r",
            LongOp = "--repair",
            Description =
                "Fix cheat table/xml open and close elements.",
            Flag = Options.Repair
        },
        new()
        {
            Name = "Compact",
            ShortOp = "-c",
            LongOp = "--compact",
            Description =
                "Removes extra new line characters.",
            Flag = Options.Compact
        },
        new()
        {
            Name = "LinearLUA",
            ShortOp = "-ll",
            LongOp = "--linearlua",
            Description =
                "Linearlizes the LUA code scripts and blocks.",
            Flag = Options.LinearLUA
        },
        /*new()
        {
            Name = "LinearASM",
            ShortOp = "-la",
            LongOp = "--linearasm",
            Description =
                "Linearlizes the ASM code scripts and blocks.",
            Flag = Options.Repair
        },*/
        new()
        {
            Name = "RemoveExtraSpaces",
            ShortOp = "-res",
            LongOp = "--removeextraspaces",
            Description =
                "Removes extra spaces that are unnecessary.",
            Flag = Options.RemoveExtraSpaces
        },
        new()
        {
            Name = "RemoveSignature",
            ShortOp = "-rsig",
            LongOp = "--removesignature",
            Description =
                "Removes the signature that signed the table.",
            Flag = Options.RemoveSignature
        },
        new()
        {
            Name = "RemoveStructures",
            ShortOp = "-rstr",
            LongOp = "--removestructures",
            Description =
                "Removes any structures bloating up the table.",
            Flag = Options.RemoveStructures
        },
        new()
        {
            Name = "RemoveUserDefinedSymbols",
            ShortOp = "-ruds",
            LongOp = "--removeuserdefinedsymbols",
            Description =
                "Removes user defined symbols.",
            Flag = Options.RemoveUserDefinedSymbols
        },
        new()
        {
            Name = "Full",
            ShortOp = "-f",
            LongOp = "--full",
            Description =
                "Uses all cleanup options. (Excluding Signature Removal)",
            Flag = Options.Full
        },
        new()
        {
            Name = "NoLinearXML",
            ShortOp = "-nlx",
            LongOp = "--nolinearxml",
            Description =
                "Prevents the linearlizing of xml.",
            Flag = Options.NoLinearXML
        }
    };

    private static string AssemblyPath = string.Empty;
    private static Options options;

    private static readonly IDictionary<string, Assembly> PossibleAssemblyDict = new Dictionary<string, Assembly>();

    private static void Main(string[] args)
    {
        var codeBase = Assembly.GetExecutingAssembly().CodeBase;
        var uri = new UriBuilder(codeBase);
        AssemblyPath = Path.GetFullPath(Uri.UnescapeDataString(uri.Path));
        var possibleLibPath = Path.Combine(Path.GetDirectoryName(AssemblyPath), "Lib");
        if (Directory.Exists(possibleLibPath))
        {
            var optionalAssemblies =
                new DirectoryInfo(possibleLibPath).GetFilesRecursive();
            foreach (var fi in optionalAssemblies)
                try
                {
                    var assembly = Assembly.Load(File.ReadAllBytes(fi.FullName));
                    PossibleAssemblyDict.Add(assembly.FullName, assembly);
                }
                catch
                {
                }

            if (optionalAssemblies.Length > 0)
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += ResolvePossibleAssembly;
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePossibleAssembly;
            }
        }

        if (ShouldGetUsage(args))
        {
            ShowUsage();
            return;
        }

        options = ProcessOptions<Options>(args, ConsoleOptions);

        var path = Path.GetFullPath(args[0]);

        var attr = new FileAttributes();
        try
        {
            attr = File.GetAttributes(path);
        }
        catch (Exception ex)
        {
            ErrorMessage(ex.Message);
            return;
        }


        if (attr.HasFlag(FileAttributes.Directory))
        {
            var files = new DirectoryInfo(path).GetFilesRecursive();
            foreach (var file in files) ProcessFile(file);
        }
        else
        {
            ProcessFile(new FileInfo(path));
        }
    }

    private static void ProcessFile(FileInfo file)
    {
        Console.WriteLine($"Processing: {file.FullName}");

        var document = new XmlDocument();
        Stream stream;

        if (options.HasFlag(Options.Repair))
        {
            Console.WriteLine("Repairing...");
            stream = new MemoryStream(Encoding.UTF8.GetBytes(XMLRepair(File.ReadAllText(file.FullName))));
        }
        else
        {
            try
            {
                stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (Exception ex)
            {
                ErrorMessage(ex.Message);
                return;
            }
        }

        document.Load(stream);
        var navigator = document.CreateNavigator();

        // Increment IDs to clean up unnecessarily high ID values.
        var count = 1;
        foreach (XPathNavigator nav in navigator.Select("//ID"))
        {
            nav.SetValue(count.ToString());
            count++;
        }

        var removeNodeList = new List<XPathNavigator>();

        if (options.HasFlag(Options.RemoveSignature))
        {
            Console.WriteLine("Removing signature...");
            foreach (XPathNavigator nav in navigator.Select("//Signature"))
                removeNodeList.Add(nav);
        }

        if (options.HasFlag(Options.RemoveStructures))
        {
            Console.WriteLine("Removing structures...");
            foreach (XPathNavigator nav in navigator.Select("//Structures"))
                removeNodeList.Add(nav);
        }

        if (options.HasFlag(Options.RemoveUserDefinedSymbols))
        {
            Console.WriteLine("Removing user defined symbols...");
            foreach (XPathNavigator nav in navigator.Select("//UserdefinedSymbols"))
                removeNodeList.Add(nav);
        }

        if (options.HasFlag(Options.LinearLUA))
        {
            Console.WriteLine("Linearizing LUA...");
            SubtleMessage("Also trimming for safety...");
            foreach (XPathNavigator nav in navigator.Select("//LuaScript"))
                nav.SetValue(LinearizeLUA(LinearizeLUA(nav.Value, true)));

            foreach (XPathNavigator nav in navigator.Select("//AssemblerScript"))
                nav.SetValue(LinearizeLUA(LinearizeLUA(nav.Value)));
        }

        Console.WriteLine("Removing redundancies...");

        // Remove redundant parameters

        foreach (XPathNavigator nav in navigator.Select("//LastState")) removeNodeList.Add(nav);

        foreach (XPathNavigator nav in navigator.Select("//Unicode"))
            if (nav.ValueAsInt == 0)
                removeNodeList.Add(nav);

        foreach (XPathNavigator nav in navigator.Select("//CodePage"))
            if (nav.ValueAsInt == 0)
                removeNodeList.Add(nav);

        foreach (XPathNavigator nav in navigator.Select("//ZeroTerminate"))
            if (nav.ValueAsInt == 1)
                removeNodeList.Add(nav);

        foreach (XPathNavigator nav in navigator.Select("//Color"))
            if (nav.Value == "000000")
                removeNodeList.Add(nav);

        foreach (XPathNavigator nav in navigator.Select("//DropDownList"))
            if (string.IsNullOrWhiteSpace(nav.Value))
                removeNodeList.Add(nav);

        foreach (var nav in removeNodeList)
            nav.DeleteSelf();

        removeNodeList.Clear();

        // Create new data stream

        var memStream = new MemoryStream();

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = options.HasFlag(Options.NoLinearXML)
        };
        using (var writer = XmlWriter.Create(memStream, settings))
        {
            document.Save(writer);
        }

        stream.Close();
        stream.Dispose();

        var xmlStr = Encoding.UTF8.GetString(memStream.ToArray());
        memStream.Close();
        memStream.Dispose();

        // Final edits

        var multiLineRegex = new Regex(@"(\r?\n)*(\s?)*(\r?\n)");

        xmlStr = xmlStr.Replace(@" \>", @"\>");
        if (options.HasFlag(Options.Compact))
        {
            Console.WriteLine("Compacting...");
            xmlStr = multiLineRegex.Replace(multiLineRegex.Replace(xmlStr, Environment.NewLine), Environment.NewLine);
        }

        if (options.HasFlag(Options.RemoveExtraSpaces))
        {
            Console.WriteLine("Removing extra spaces...");
            xmlStr = RemoveExtraSpaces(RemoveExtraSpaces(xmlStr));
        }

        // Save file and backup

        Console.WriteLine("Saving...");

        var newFilePath = Path.Combine(Path.GetDirectoryName(file.FullName),
            Path.GetFileNameWithoutExtension(file.FullName) + "_Cleaned.CT");

        File.WriteAllBytes(newFilePath, Encoding.UTF8.GetBytes(xmlStr));

        File.Replace(newFilePath, file.FullName, file.FullName + ".bak");

        Console.WriteLine("Complete!");
    }

    private static string XMLRepair(string xml)
    {
        var result = xml;

        // Fix missing opening and closing elements

        var regexStrClose = @"[<]([^>]+?(?=[<]|\Z))";
        var regexStrOpen = @"(\A|[>])[^<]+?[>]";
        var regexClose = new Regex(regexStrClose);
        var regexOpen = new Regex(regexStrOpen);


        result = regexClose.Replace(xml, delegate(Match match)
        {
            return match.Value.Insert(
                match.Value.Length - match.Value.Reverse().TakeWhile(c => char.IsWhiteSpace(c)).Count(), ">");
        });

        result = regexOpen.Replace(result, delegate(Match match)
        {
            var notBeginning = match.Value[0] == '>';
            return match.Value.Insert(
                match.Value.Skip(Convert.ToInt32(notBeginning)).TakeWhile(c => char.IsWhiteSpace(c)).Count() +
                Convert.ToInt32(notBeginning), "<");
        });

        return result;
    }

    private static string RemoveExtraSpaces(string str)
    {
        var inQuotes = false;
        var lastChar = '\x00';
        for (var i = 0; i < str.Length; i++)
        {
            inQuotes ^= str[i] == '"';
            if (!inQuotes && str[i] == ' ' && lastChar == ' ')
            {
                str = str.Remove(i, 1);
                i--;
            }

            lastChar = str[i];
        }

        return str;
    }

    private static string LinearizeLUA(string str, bool alwaysLua = false)
    {
        str = string.Join("\r\n", str.Split(new[] {"\r\n"}, StringSplitOptions.None).Select(s => s.Trim()).ToArray());
        var inLUA = alwaysLua;
        var transitioning = false;
        var inMultiLineString = false;
        var inQuotes = false;
        var luaComment = "--";
        var luaMultiLineStart = "[[";
        var luaMultiLineEnd = "]]";
        var luaBlockInit = "{$lua}";
        var asmBlockInit = "{$asm}";
        var enableBlockInit = "[ENABLE]";
        var disableBlockInit = "[DISABLE]";
        for (var i = 0; i < str.Length; i++)
        {
            inQuotes ^= str[i] == '"';
            if ((inLUA || alwaysLua) && i <= str.Length - luaMultiLineStart.Length &&
                str.Substring(i, luaMultiLineStart.Length) == luaMultiLineStart)
            {
                inMultiLineString = true;
            }
            else if ((inLUA || alwaysLua) && i <= str.Length - luaMultiLineEnd.Length &&
                     str.Substring(i, luaMultiLineEnd.Length) == luaMultiLineEnd)
            {
                inMultiLineString = false;
            }
            else if (i <= str.Length - luaBlockInit.Length &&
                     str.Substring(i, luaBlockInit.Length).ToLower() == luaBlockInit)
            {
                inLUA = true;
                transitioning = true;
            }
            else if (i <= str.Length - asmBlockInit.Length &&
                     str.Substring(i, asmBlockInit.Length).ToLower() == asmBlockInit)
            {
                inLUA = false;
                transitioning = true;
            }
            else if (i <= str.Length - enableBlockInit.Length &&
                     str.Substring(i, enableBlockInit.Length).ToUpper() == enableBlockInit)
            {
                transitioning = true;
            }
            else if (i <= str.Length - disableBlockInit.Length &&
                     str.Substring(i, disableBlockInit.Length).ToUpper() == disableBlockInit)
            {
                transitioning = true;
            }

            var procedingWhitespaces = str.Skip(i).TakeWhile(c => char.IsWhiteSpace(c)).Count();

            if (!inQuotes && !inMultiLineString && (inLUA || alwaysLua) &&
                (str[i] == '\n' || i <= str.Length - 2 && str.Substring(i, 2) == "\r\n") &&
                !(i <= str.Length - (asmBlockInit.Length + procedingWhitespaces) &&
                  str.Substring(i + procedingWhitespaces, asmBlockInit.Length).ToLower() == asmBlockInit) &&
                !(i <= str.Length - (enableBlockInit.Length + procedingWhitespaces) &&
                  str.Substring(i + procedingWhitespaces, enableBlockInit.Length).ToUpper() == enableBlockInit) &&
                !(i <= str.Length - (disableBlockInit.Length + procedingWhitespaces) &&
                  str.Substring(i + procedingWhitespaces, disableBlockInit.Length).ToUpper() == disableBlockInit))
            {
                if (str[i] == '\n')
                {
                    if (!transitioning)
                        str = str.Remove(i, 1).Insert(i, " ");
                    else
                        transitioning = false;
                }
                else if (str.Substring(i, 2) == "\r\n")
                {
                    if (!transitioning)
                    {
                        str = str.Remove(i, 2).Insert(i, " ");
                        i--;
                    }
                    else
                    {
                        transitioning = false;
                        i++;
                    }
                }
            }

            if (!inQuotes && !inMultiLineString && (inLUA || alwaysLua) && i <= str.Length - luaComment.Length &&
                str.Substring(i, luaComment.Length) == luaComment && i <= str.Length - 4 &&
                str.Substring(i + luaComment.Length, luaMultiLineStart.Length) != luaMultiLineStart)
            {
                str = str.Insert(i + luaComment.Length, luaMultiLineStart);
                str = str.Insert(i + str.Skip(i).TakeWhile(c => c != '\r' && c != '\n').Count(), luaMultiLineEnd);
            }
        }

        return str;
    }

    private static int FindBytes(byte[] src, byte[] find)
    {
        var index = -1;
        var matchIndex = 0;
        for (var i = 0; i < src.Length; i++)
            if (src[i] == find[matchIndex])
            {
                if (matchIndex == find.Length - 1)
                {
                    index = i - matchIndex;
                    break;
                }

                matchIndex++;
            }
            else if (src[i] == find[0])
            {
                matchIndex = 1;
            }
            else
            {
                matchIndex = 0;
            }

        return index;
    }

    public static byte[] ReplaceBytes(byte[] src, byte[] search, byte[] repl)
    {
        byte[] dst = null;
        byte[] temp = null;
        var index = FindBytes(src, search);
        while (index >= 0)
        {
            if (temp == null)
                temp = src;
            else
                temp = dst;

            dst = new byte[temp.Length - search.Length + repl.Length];

            Buffer.BlockCopy(temp, 0, dst, 0, index);
            Buffer.BlockCopy(repl, 0, dst, index, repl.Length);
            Buffer.BlockCopy(
                temp,
                index + search.Length,
                dst,
                index + repl.Length,
                temp.Length - (index + search.Length));


            index = FindBytes(dst, search);
        }

        return dst;
    }

    private static void ShowUsage()
    {
        ConsoleTools.ShowUsage(
            $"Usage: {Path.GetFileName(AssemblyPath)} <file/folder path> [options...]", ConsoleOptions);
    }

    private static Assembly ResolvePossibleAssembly(object sender, ResolveEventArgs e)
    {
        PossibleAssemblyDict.TryGetValue(e.Name, out var res);
        return res;
    }

    [Flags]
    private enum Options
    {
        Repair = 0x1,
        Compact = 0x2,
        LinearLUA = 0x4,

        //LinearASM = 0x8,
        RemoveExtraSpaces = 0x10,
        RemoveSignature = 0x20,
        RemoveStructures = 0x40,
        RemoveUserDefinedSymbols = 0x80,
        Full = 0xFFFFFDF,
        NoLinearXML = 0x10000000
    }
}