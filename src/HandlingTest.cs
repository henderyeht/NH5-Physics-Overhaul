using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace SimHandling
{
    internal static class HandlingTest
    {
        public static int Run()
        {
            var root = @"E:\My Stuff\NH5\Tools\Sim Handling";
            var work = Path.Combine(root, "work");
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }

            Directory.CreateDirectory(work);
            HandlingPatch.BackupRoot = Path.Combine(work, "backups");
            var sources = new[]
            {
                Path.Combine(root, "reference", "base-steam.dll"),
                Path.Combine(root, "reference", "nextgen-live.dll"),
                Path.Combine(root, "reference", "season-2025.dll"),
                @"E:\My Stuff\NH5\Mods\2003\Nascar Heat 5 - 2003\NASCARHeat5_Data\Managed\Assembly-CSharp.dll",
                @"E:\My Stuff\NH5\Mods\NASCAR Heat 5 1992\NASCAR Heat -1992\NASCARHeat5_Data\Managed\Assembly-CSharp.dll"
            };
            var before = new string[sources.Length];
            for (var i = 0; i < sources.Length; i++)
            {
                before[i] = Hash(sources[i]);
            }

            var engine = BuildEngine();
            var failures = 0;
            var logPath = Path.Combine(root, "test-log.txt");
            File.WriteAllText(logPath, "");
            for (var i = 0; i < sources.Length; i++)
            {
                var name = "copy-" + i;
                var folder = Path.Combine(work, name, "NASCARHeat5_Data", "Managed");
                Directory.CreateDirectory(folder);
                var dll = Path.Combine(folder, "Assembly-CSharp.dll");
                try
                {
                    File.Copy(sources[i], dll, true);
                    CheckOne(dll, engine);
                    File.AppendAllText(logPath, "PASS " + name + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    failures++;
                    File.AppendAllText(logPath, "FAIL " + name + " " + ex.Message + Environment.NewLine);
                }
            }

            for (var i = 0; i < sources.Length; i++)
            {
                var now = Hash(sources[i]);
                if (now != before[i])
                {
                    failures++;
                    Console.WriteLine("FAIL source changed " + sources[i]);
                }
            }

            File.AppendAllText(logPath, failures == 0 ? "ALL PASS" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static void CheckOne(string dll, string engine)
        {
            var stock = File.ReadAllBytes(dll);
            var applied = HandlingPatch.Apply(dll);
            if (!applied.Applied)
            {
                throw new InvalidOperationException("Apply did not mark the DLL.");
            }

            var patched = File.ReadAllBytes(dll);
            if (patched.Length != stock.Length)
            {
                throw new InvalidOperationException("Size changed.");
            }

            var hadAi = Contains(stock, EarlyRet(0x2A)) || Contains(stock, EarlyRet(0x00));
            var hasAi = Contains(patched, EarlyRet(0x2A)) || Contains(patched, EarlyRet(0x00));
            if (hadAi && !hasAi)
            {
                throw new InvalidOperationException("AI skill pattern was overwritten.");
            }

            if (CountFloat(stock, 720f) != CountFloat(patched, 720f))
            {
                throw new InvalidOperationException("Engine power values changed.");
            }

            var grip = Process.Start(new ProcessStartInfo
            {
                FileName = engine,
                Arguments = "--grip-test \"" + dll + "\" 150",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            var output = grip.StandardOutput.ReadToEnd() + grip.StandardError.ReadToEnd();
            grip.WaitForExit();
            var gripOk = grip.ExitCode == 0 && output.IndexOf("-> stock 150", StringComparison.Ordinal) >= 0;
            if (!gripOk)
            {
                if (output.IndexOf("power ", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException("Engine grip overwrite failed: " + output.Trim());
                }

                if (!HandlingPatch.IsApplied(dll))
                {
                    throw new InvalidOperationException("Physics did not stay applied.");
                }

                HandlingPatch.Remove(dll);
                if (HandlingPatch.IsApplied(dll))
                {
                    throw new InvalidOperationException("Remove left the tire curve on.");
                }

                return;
            }

            var gripped = CountFloat(File.ReadAllBytes(dll), 1.2f * 1.5f);

            if (!HandlingPatch.IsApplied(dll))
            {
                throw new InvalidOperationException("Engine grip removed the tire curve.");
            }

            HandlingPatch.Remove(dll);
            if (HandlingPatch.IsApplied(dll))
            {
                throw new InvalidOperationException("Remove left the tire curve on.");
            }

            if (CountFloat(File.ReadAllBytes(dll), 1.2f * 1.5f) != gripped)
            {
                throw new InvalidOperationException("Remove wiped the engine grip.");
            }
        }

        private static string BuildEngine()
        {
            var project = @"E:\My Stuff\NH5\Tools\Engine Modifier Tool\src\EngineModifierTool\EngineModifierTool.csproj";
            var build = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build \"" + project + "\" -c Release --nologo -v q",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            var text = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
            build.WaitForExit();
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(text);
            }

            return @"E:\My Stuff\NH5\Tools\Engine Modifier Tool\src\EngineModifierTool\bin\Release\EngineModifierTool.exe";
        }

        private static byte[] EarlyRet(byte flag)
        {
            return new byte[] { 64, 1, 0, 0, 0, flag, 7, 3, 40 };
        }

        private static bool Contains(byte[] hay, byte[] needle)
        {
            for (var i = 0; i <= hay.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (hay[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountFloat(byte[] bytes, float value)
        {
            var raw = BitConverter.GetBytes(value);
            var count = 0;
            for (var i = 0; i < bytes.Length - 4; i++)
            {
                if (bytes[i] == raw[0] && bytes[i + 1] == raw[1] && bytes[i + 2] == raw[2] && bytes[i + 3] == raw[3])
                {
                    count++;
                }
            }

            return count;
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream));
            }
        }
    }
}
