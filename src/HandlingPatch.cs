using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SimHandling
{
    internal sealed class HandlingResult
    {
        public int Changed;
        public bool Applied;
    }

    internal static class HandlingPatch
    {
        public static string BackupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NH5 Sim Handling",
            "Backups");

        public static bool IsApplied(string dllPath)
        {
            var bytes = File.ReadAllBytes(dllPath);
            return ReadFloat(bytes, "SprintCupChassis", "make_default_axle", "magic_c") == 1.3f;
        }

        public static HandlingResult Apply(string dllPath)
        {
            return Run(dllPath, remove: false);
        }

        public static HandlingResult Remove(string dllPath)
        {
            return Run(dllPath, remove: true);
        }

        private static HandlingResult Run(string dllPath, bool remove)
        {
            if (IsProtected(dllPath))
            {
                throw new InvalidOperationException("That path is a mod folder. Pick the game install.");
            }

            var original = File.ReadAllBytes(dllPath);
            var bytes = (byte[])original.Clone();
            var saved = LoadSaved(dllPath);
            var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { InMemory = true });
            var changed = 0;
            foreach (var edit in Edits())
            {
                var method = FindMethod(assembly, edit.Type, edit.Method);
                if (method == null)
                {
                    if (edit.Required)
                    {
                        throw new InvalidOperationException("Missing " + edit.Type + "." + edit.Method + ".");
                    }

                    continue;
                }

                if (edit.PresetIndex >= 0)
                {
                    changed += Preset(bytes, method, edit, saved, remove);
                    continue;
                }

                changed += Field(bytes, method, edit, saved, remove);
            }

            if (!remove && changed < 6)
            {
                throw new InvalidOperationException("This DLL does not have the Heat 5 handling layout.");
            }

            if (changed > 0)
            {
                KeepBackup(dllPath, original);
                Save(dllPath, saved);
                Write(dllPath, bytes);
            }

            return new HandlingResult { Changed = changed, Applied = IsApplied(dllPath) };
        }

        private static int Field(byte[] bytes, MethodDefinition method, Edit edit, Dictionary<string, float> saved, bool remove)
        {
            var load = LoadBeforeStore(method, edit.Field);
            if (load == null)
            {
                if (edit.Required)
                {
                    throw new InvalidOperationException(method.DeclaringType.Name + "." + edit.Field + " was not found.");
                }

                return 0;
            }

            var current = (float)load.Operand;
            var offset = FileOffset(bytes, method, load);
            var key = method.DeclaringType.Name + "|" + Trim(method.Name) + "|" + edit.Field;
            if (!remove)
            {
                if (current == edit.Value)
                {
                    return 0;
                }

                if (!saved.ContainsKey(key))
                {
                    saved[key] = current;
                }

                WriteFloat(bytes, offset, edit.Value);
                return 1;
            }

            if (current != edit.Value)
            {
                return 0;
            }

            if (!saved.TryGetValue(key, out var prior))
            {
                prior = edit.Stock;
            }

            WriteFloat(bytes, offset, prior);
            return 1;
        }

        private static int Preset(byte[] bytes, MethodDefinition method, Edit edit, Dictionary<string, float> saved, bool remove)
        {
            var loads = new List<Instruction>();
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Ldc_R4)
                {
                    loads.Add(instruction);
                }
            }

            if (loads.Count < 30)
            {
                throw new InvalidOperationException("Driving aid presets are not the Heat 5 layout.");
            }

            var load = loads[edit.PresetIndex];
            var current = (float)load.Operand;
            var offset = FileOffset(bytes, method, load);
            var key = "preset|" + edit.PresetIndex;
            if (!remove)
            {
                if (!saved.ContainsKey(key))
                {
                    saved[key] = current;
                }

                if (current == edit.Value)
                {
                    return 0;
                }

                WriteFloat(bytes, offset, edit.Value);
                return 1;
            }

            if (!saved.TryGetValue(key, out var prior))
            {
                prior = edit.Stock;
            }

            if (current == prior)
            {
                return 0;
            }

            WriteFloat(bytes, offset, prior);
            return 1;
        }

        private static Instruction LoadBeforeStore(MethodDefinition method, string field)
        {
            Instruction load = null;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Stfld || instruction.Operand is not FieldReference target || target.Name != field)
                {
                    continue;
                }

                var previous = instruction.Previous;
                if (previous == null || previous.OpCode != OpCodes.Ldc_R4)
                {
                    continue;
                }

                if (load != null)
                {
                    throw new InvalidOperationException(method.DeclaringType.Name + " stores " + field + " more than once.");
                }

                load = previous;
            }

            return load;
        }

        private static float ReadFloat(byte[] bytes, string typeName, string methodName, string field)
        {
            var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { InMemory = true });
            var method = FindMethod(assembly, typeName, methodName);
            var load = method == null ? null : LoadBeforeStore(method, field);
            if (load == null)
            {
                return float.NaN;
            }

            return (float)load.Operand;
        }

        private static MethodDefinition FindMethod(AssemblyDefinition assembly, string typeName, string methodName)
        {
            foreach (var type in All(assembly.MainModule.Types))
            {
                if (type.Name != typeName)
                {
                    continue;
                }

                if (typeName == "Physics" && type.Namespace.IndexOf("D2D", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                foreach (var method in type.Methods)
                {
                    if (method.HasBody && method.Name.IndexOf(methodName, StringComparison.Ordinal) >= 0)
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types)
        {
            foreach (var type in types)
            {
                yield return type;
                foreach (var nested in All(type.NestedTypes))
                {
                    yield return nested;
                }
            }
        }

        private static void WriteFloat(byte[] bytes, int offset, float value)
        {
            if (bytes[offset] != 0x22)
            {
                throw new InvalidOperationException("Handling value is not a float instruction.");
            }

            var next = BitConverter.GetBytes(value);
            Buffer.BlockCopy(next, 0, bytes, offset + 1, 4);
        }

        private static int FileOffset(byte[] bytes, MethodDefinition method, Instruction instruction)
        {
            var position = RvaToOffset(bytes, (int)method.RVA);
            var flags = BitConverter.ToUInt16(bytes, position);
            var start = (flags & 0x3) == 0x2 ? position + 1 : position + ((flags >> 12) * 4);
            return start + instruction.Offset;
        }

        private static int RvaToOffset(byte[] bytes, int rva)
        {
            var pe = BitConverter.ToInt32(bytes, 0x3C);
            var sections = BitConverter.ToInt16(bytes, pe + 6);
            var optional = BitConverter.ToInt16(bytes, pe + 20);
            var section = pe + 24 + optional;
            for (var i = 0; i < sections; i++)
            {
                var at = section + i * 40;
                var virtualAddress = BitConverter.ToInt32(bytes, at + 12);
                var rawPointer = BitConverter.ToInt32(bytes, at + 20);
                var span = Math.Max(BitConverter.ToInt32(bytes, at + 8), BitConverter.ToInt32(bytes, at + 16));
                if (rva >= virtualAddress && rva < virtualAddress + span)
                {
                    return rawPointer + (rva - virtualAddress);
                }
            }

            throw new InvalidOperationException("Could not locate the handling method.");
        }

        private static void KeepBackup(string dllPath, byte[] original)
        {
            var dest = Path.Combine(BackupRoot, Slot(dllPath), "Assembly-CSharp.dll");
            if (File.Exists(dest))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllBytes(dest, original);
        }

        private static Dictionary<string, float> LoadSaved(string dllPath)
        {
            var map = new Dictionary<string, float>(StringComparer.Ordinal);
            var path = Sidecar(dllPath);
            if (!File.Exists(path))
            {
                return map;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var cut = line.IndexOf('=');
                if (cut <= 0)
                {
                    continue;
                }

                if (float.TryParse(line.Substring(cut + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    map[line.Substring(0, cut)] = value;
                }
            }

            return map;
        }

        private static void Save(string dllPath, Dictionary<string, float> saved)
        {
            var path = Sidecar(dllPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var text = new StringBuilder();
            foreach (var pair in saved)
            {
                text.Append(pair.Key).Append('=').Append(pair.Value.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }

            File.WriteAllText(path, text.ToString());
        }

        private static string Sidecar(string dllPath)
        {
            return Path.Combine(BackupRoot, Slot(dllPath), "handling.txt");
        }

        private static string Slot(string dllPath)
        {
            var hash = 0;
            foreach (var ch in Path.GetFullPath(dllPath).ToLowerInvariant())
            {
                hash = unchecked(hash * 31 + ch);
            }

            return "dll-" + unchecked((uint)hash).ToString("x8");
        }

        private static void Write(string dllPath, byte[] bytes)
        {
            var tmp = dllPath + ".sim.tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(dllPath))
            {
                var attrs = File.GetAttributes(dllPath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(dllPath, attrs & ~FileAttributes.ReadOnly);
                }
            }

            File.Copy(tmp, dllPath, true);
            File.Delete(tmp);
        }

        public static bool IsProtected(string path)
        {
            var full = Path.GetFullPath(path);
            return full.IndexOf(@"\Mods\", StringComparison.OrdinalIgnoreCase) >= 0
                || full.IndexOf(@"\NH5 Multiplayer Revival\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Trim(string name)
        {
            var dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        private static IEnumerable<Edit> Edits()
        {
            // Pacejka lateral shape C=1.3. Peak solved at 4 deg (Crowe: NASCAR slip is 2-4 deg).
            // B = tan(pi / (2*C)) / 4. The 10.9% fade is 1 - sin(C*pi/2), spread across the stock 3 deg window.
            // mu(800 lb)=1.437792 and mu(3000 lb)=1.131201 use Milliken's load exponent on a 2 G Indy corner.
            const float peakB = 0.6591958f;
            const float fade = 0.1089935f;
            const float muLight = 1.437792f;
            const float muHeavy = 1.131201f;
            const float muLong = 1.310076f;
            foreach (var type in new[] { "SprintCupChassis", "XfinityChassis", "TruckChassis", "SprintCupChassisDLC" })
            {
                var required = type != "SprintCupChassisDLC";
                yield return F(type, "make_default_axle", "magic_b", peakB, 1f, required);
                yield return F(type, "make_default_axle", "magic_c", 1.3f, 1f, required);
                yield return F(type, "make_default_axle", "grip_fade", fade, 0.05f, required);
                yield return F(type, "make_default_axle", "grip_fade_start", 4f, 9f, required);
                yield return F(type, "make_default_axle", "grip_fade_stop", 7f, 12f, required);
                yield return F(type, "make_default_axle", "frict_k1", muLight, 1.2f, required);
                yield return F(type, "make_default_axle", "frict_k2", muHeavy, 1.1f, required);
                yield return F(type, "make_default_axle", "long_frict", muLong, 1.1f, required);
                yield return F(type, "MakeLiteFrontAxle", "magic_b", peakB, 0.714f, required);
                yield return F(type, "MakeLiteFrontAxle", "magic_c", 1.3f, 1.4f, required);
                yield return F(type, "MakeLiteFrontAxle", "magic_e", 0f, -0.2f, required);
                yield return F(type, "MakeLiteRearAxle", "magic_b", peakB, 0.714f, required);
                yield return F(type, "MakeLiteRearAxle", "magic_c", 1.3f, 1.4f, required);
                yield return F(type, "MakeLiteRearAxle", "magic_e", 0f, -0.2f, required);
            }

            foreach (var type in new[] { "DirtBankedChassis", "DirtFlatChassis", "DirtRoadChassis", "EldoraTruckChassis" })
            {
                yield return F(type, "make_default_axle", "magic_b", 0.1757856f, 1f, true);
                yield return F(type, "make_default_axle", "magic_c", 1.3f, 1f, true);
            }

            yield return F("SprintCupChassis", "MakeChassis", "drag", 0.37f, 0.3f, true);
            yield return F("SprintCupChassis", "MakeChassis", "front_lift", -0.503615f, -0.335f, true);
            yield return F("SprintCupChassis", "MakeChassis", "rear_lift", -0.667583f, -0.36f, true);
            yield return F("SprintCupChassis", "make_body_superspeedway", "drag", 0.407f, 0.33f, true);
            yield return F("SprintCupChassis", "make_body_superspeedway", "front_lift", -0.286480f, -0.17f, true);
            yield return F("SprintCupChassis", "make_body_superspeedway", "rear_lift", -0.505553f, -0.3f, true);
            yield return F("XfinityChassis", "MakeChassis", "drag", 0.37f, 0.3f, true);
            yield return F("XfinityChassis", "MakeChassis", "front_lift", -0.503615f, -0.335f, true);
            yield return F("XfinityChassis", "MakeChassis", "rear_lift", -0.667583f, -0.36f, true);
            yield return F("XfinityChassis", "make_body_superspeedway", "drag", 0.37f, 0.3f, true);
            yield return F("XfinityChassis", "make_body_superspeedway", "front_lift", -0.286480f, -0.17f, true);
            yield return F("XfinityChassis", "make_body_superspeedway", "rear_lift", -0.505553f, -0.3f, true);
            yield return F("TruckChassis", "MakeChassis", "drag", 0.37f, 0.3f, true);
            yield return F("TruckChassis", "MakeChassis", "front_lift", -0.580981f, -0.34476f, true);
            yield return F("TruckChassis", "MakeChassis", "rear_lift", -0.623094f, -0.36975f, true);
            yield return F("TruckChassis", "make_body_superspeedway", "drag", 0.37f, 0.3f, true);
            yield return F("TruckChassis", "make_body_superspeedway", "front_lift", -0.286480f, -0.17f, true);
            yield return F("TruckChassis", "make_body_superspeedway", "rear_lift", -0.505553f, -0.3f, true);
            yield return F("SprintCupChassisDLC", "MakeChassis", "drag", 0.543314f, 0.44f, false);
            yield return F("SprintCupChassisDLC", "MakeChassis", "front_lift", -0.308812f, -0.317f, false);
            yield return F("SprintCupChassisDLC", "MakeChassis", "rear_lift", -0.720561f, -0.517f, false);
            yield return F("SprintCupChassisDLC", "make_body_superspeedway", "drag", 0.659150f, 0.53f, false);
            yield return F("SprintCupChassisDLC", "make_body_superspeedway", "front_lift", -0.419954f, -0.338f, false);
            yield return F("SprintCupChassisDLC", "make_body_superspeedway", "rear_lift", -0.866762f, -0.697f, false);
            yield return F("Physics", "MakeDefault", "max_draft_lon_carlen", 3f, 18.85f, true);
            yield return F("Physics", "MakeDefault", "min_draft_width_carwid", 0.125f, 0.763f, true);
            foreach (var method in new[] { "SetRestrictorPackage_Speedway_WithDucts", "SetRestrictorPackage_Speedway_NoDucts" })
            {
                yield return F("Physics", method, "max_draft_lon_carlen", 3f, 12f, true);
                yield return F("Physics", method, "min_draft_width_carwid", 0.125f, 1f, true);
            }
        }

        private static Edit F(string type, string method, string field, float value, float stock, bool required)
        {
            return new Edit { Type = type, Method = method, Field = field, Value = value, Stock = stock, Required = required, PresetIndex = -1 };
        }

        private sealed class Edit
        {
            public string Type = "";
            public string Method = "";
            public string Field = "";
            public float Value;
            public float Stock;
            public bool Required;
            public int PresetIndex;
        }
    }
}
