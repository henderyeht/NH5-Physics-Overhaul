using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SimHandling
{
    internal sealed class GameInstall
    {
        public string Root = "";
        public string Dll = "";
    }

    internal static class Installs
    {
        private static string ListPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NH5 Sim Handling",
            "installs.txt");

        public static List<GameInstall> Find()
        {
            var list = new List<GameInstall>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in Candidates())
            {
                var full = Path.GetFullPath(root);
                if (!seen.Add(full))
                {
                    continue;
                }

                var dll = Path.Combine(full, "NASCARHeat5_Data", "Managed", "Assembly-CSharp.dll");
                if (!File.Exists(dll) || HandlingPatch.IsProtected(dll))
                {
                    continue;
                }

                list.Add(new GameInstall { Root = full, Dll = dll });
            }

            return list;
        }

        public static void Remember(string root)
        {
            var lines = new List<string>();
            if (File.Exists(ListPath))
            {
                lines.AddRange(File.ReadAllLines(ListPath));
            }

            if (!lines.Exists(line => string.Equals(line, root, StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add(root);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
            File.WriteAllLines(ListPath, lines);
        }

        public static string Resolve(string picked)
        {
            if (File.Exists(Path.Combine(picked, "NASCARHeat5.exe")) || Directory.Exists(Path.Combine(picked, "NASCARHeat5_Data")))
            {
                return picked;
            }

            var parent = Directory.GetParent(picked);
            if (parent != null && File.Exists(Path.Combine(parent.FullName, "NASCARHeat5.exe")))
            {
                return parent.FullName;
            }

            return "";
        }

        private static IEnumerable<string> Candidates()
        {
            if (File.Exists(ListPath))
            {
                foreach (var line in File.ReadAllLines(ListPath))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        yield return line.Trim();
                    }
                }
            }

            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrWhiteSpace(steam))
            {
                steam = steam.Replace('/', '\\');
                yield return Path.Combine(steam, "steamapps", "common", "NASCAR Heat 5");
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    {
                        yield return Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps", "common", "NASCAR Heat 5");
                    }
                }
            }

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using (var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null)
                    {
                        continue;
                    }

                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        using (var key = uninstall.OpenSubKey(name))
                        {
                            var display = key?.GetValue("DisplayName") as string;
                            if (display == null || display.IndexOf("NASCAR Heat 5", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }

                            var location = (key.GetValue("InstallLocation") as string ?? "").Trim().TrimEnd('\\');
                            if (location.Length > 0)
                            {
                                yield return location;
                            }
                        }
                    }
                }
            }
        }
    }
}
