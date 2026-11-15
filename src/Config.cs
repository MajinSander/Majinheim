// Lectura y escritura de los .cfg de BepInEx (los ajustes de cada mod), sin perder comentarios ni orden.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Majinheim
{
    public class CfgEntry
    {
        public CfgFile File;
        public string Section, Key, Value, Default, Type, Description, Range;
        public List<string> Acceptable = new List<string>();
        public bool Multi;      // admite varios valores separados por comas
        public int Line;
        public string Original;
        public bool Changed { get { return Value != Original; } }
        public bool IsDefault { get { return Default != null && Value == Default; } }

        // Comprueba que el valor tiene el tipo correcto; devuelve el error o null.
        public string Validate()
        {
            var ci = CultureInfo.InvariantCulture;
            switch (Type)
            {
                case "Int32": case "Int64": case "Int16": case "Byte":
                    long l; if (!long.TryParse(Value, NumberStyles.Integer, ci, out l)) return "tiene que ser un número entero";
                    break;
                case "Single": case "Double": case "Decimal":
                    double d; if (!double.TryParse(Value, NumberStyles.Float, ci, out d)) return "tiene que ser un número (con punto para decimales, p.ej. 1.5)";
                    break;
                case "Boolean":
                    if (Value != "true" && Value != "false") return "tiene que ser true o false";
                    break;
            }
            if (Acceptable.Count > 0 && !Multi && !Acceptable.Contains(Value)) return "tiene que ser uno de: " + string.Join(", ", Acceptable);
            // "# Acceptable value range: From 1 to 10"
            var m = Range == null ? null : Regex.Match(Range, @"^From\s+(\S+)\s+to\s+(\S+)$");
            double lo, hi, v;
            if (m != null && m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, ci, out lo) && double.TryParse(m.Groups[2].Value, NumberStyles.Float, ci, out hi) &&
                double.TryParse(Value, NumberStyles.Float, ci, out v) && (v < lo || v > hi))
                return "tiene que estar entre " + m.Groups[1].Value + " y " + m.Groups[2].Value;
            return null;
        }
    }

    public class CfgFile
    {
        public string Path, Name;
        public List<string> Lines;
        public List<CfgEntry> Entries = new List<CfgEntry>();
        bool bom;

        static readonly Dictionary<string, string> Friendly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "kg.ItemDrawers", "Cajones (ItemDrawers)" },
            { "Azumatt.AzuExtendedPlayerInventory", "Inventario ampliado (AzuExtendedPlayerInventory)" },
            { "Azumatt.AzuCraftyBoxes", "Fabricar desde cofres (AzuCraftyBoxes)" },
            { "randyknapp.mods.epicloot", "Epic Loot" },
            { "Therzie.Wizardry", "Wizardry (magia)" },
            { "blacks7ar.BoatAdditions", "Barcos (BoatAdditions)" },
            { "blacks7ar.CoreWoodPieces", "Piezas de madera (CoreWoodPieces)" },
            { "advize.PlantEverything", "Plantar de todo (PlantEverything)" },
            { "xeio.MassFarming", "Cultivo en masa (MassFarming)" },
            { "fr.galathil.FarmGrid", "Cuadrícula de cultivo (FarmGrid)" },
            { "yay.spikehimself.xportal", "Portales (XPortal)" },
            { "nex.SpeedyPaths", "Caminos rápidos (SpeedyPaths)" },
            { "TastyChickenLegs.AutomaticFuel", "Combustible automático (AutomaticFuel)" },
            { "drummercraig.one_map_to_rule_them_all", "Mapa compartido (One Map)" },
            { "CW_Jesse.BetterNetworking", "Mejor red (BetterNetworking)" },
            { "offspringportal.mod", "Offspring Portal" },
            { "reef.characters", "Personajes en el server (ReefCharacters)" },
            { "com.jotunn.jotunn", "Jotunn (librería)" },
            { "BepInEx", "BepInEx (avanzado)" },
        };

        // Ajustes de mods que ya no están en el modpack: no se enseñan
        public static readonly string[] Hidden = { "mgdev.epiclootextrasettings", "org.bepinex.plugins.servercharacters", "FixItFelix.AutoMapPins", "com.rossdwest.itemdrawers" };

        public static CfgFile Load(string path)
        {
            var f = new CfgFile { Path = path };
            string id = System.IO.Path.GetFileNameWithoutExtension(path);
            string n;
            f.Name = Friendly.TryGetValue(id, out n) ? n : id;
            var bytes = System.IO.File.ReadAllBytes(path);
            f.bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = Encoding.UTF8.GetString(bytes, f.bom ? 3 : 0, bytes.Length - (f.bom ? 3 : 0));
            f.Lines = text.Replace("\r\n", "\n").Split('\n').ToList();

            string section = "", desc = "", type = null, def = null, range = null;
            var acc = new List<string>();
            bool multi = false;
            for (int i = 0; i < f.Lines.Count; i++)
            {
                string l = f.Lines[i].Trim();
                Match m;
                if (l == "") continue;
                if ((m = Regex.Match(l, @"^\[(.+)\]$")).Success) { section = m.Groups[1].Value; desc = ""; type = def = range = null; acc = new List<string>(); multi = false; continue; }
                if (l.StartsWith("## ")) { desc += (desc == "" ? "" : " ") + l.Substring(3); continue; }
                if (l.StartsWith("# Setting type: ")) { type = l.Substring(16).Trim(); continue; }
                if (l.StartsWith("# Default value:")) { def = l.Substring(16).Trim(); continue; }
                if (l.StartsWith("# Acceptable values: ")) { acc = l.Substring(21).Split(',').Select(x => x.Trim()).Where(x => x != "").ToList(); continue; }
                if (l.StartsWith("# Acceptable value range: ")) { range = l.Substring(26).Trim(); continue; }
                if (l.StartsWith("# Multiple values can be set")) { multi = true; continue; }
                if (l.StartsWith("#")) continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                var e = new CfgEntry
                {
                    File = f, Section = section, Key = l.Substring(0, eq).Trim(), Value = l.Substring(eq + 1).Trim(),
                    Default = def, Type = type ?? (def == null && (l.EndsWith("= true") || l.EndsWith("= false")) ? "Boolean" : null),
                    Description = desc, Range = range, Acceptable = acc, Multi = multi, Line = i
                };
                if (e.Type == "Boolean" && e.Acceptable.Count == 0) e.Acceptable = new List<string> { "true", "false" };
                e.Original = e.Value;
                f.Entries.Add(e);
                desc = ""; type = def = range = null; acc = new List<string>(); multi = false;
            }
            return f;
        }

        public bool Dirty { get { return Entries.Any(e => e.Changed); } }

        public void Save()
        {
            foreach (var e in Entries.Where(x => x.Changed)) f_SetLine(e);
            var text = string.Join("\r\n", Lines);
            var enc = new UTF8Encoding(bom);
            System.IO.File.WriteAllText(Path, text, enc);
            foreach (var e in Entries) e.Original = e.Value;
        }

        void f_SetLine(CfgEntry e)
        {
            string old = Lines[e.Line];
            int indent = old.Length - old.TrimStart().Length;
            Lines[e.Line] = old.Substring(0, indent) + e.Key + " = " + e.Value;
        }

        public static List<CfgFile> LoadAll(string configDir)
        {
            var list = new List<CfgFile>();
            if (!Directory.Exists(configDir)) return list;
            foreach (var p in Directory.GetFiles(configDir, "*.cfg"))
            {
                string id = System.IO.Path.GetFileNameWithoutExtension(p);
                if (Hidden.Any(h => h.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                try { var f = Load(p); if (f.Entries.Count > 0) list.Add(f); }
                catch (Exception e) { Log.W("No pude leer {0}: {1}", p, e.Message); }
            }
            return list.OrderBy(f => f.Name.StartsWith("BepInEx") || f.Name.StartsWith("Jotunn") ? 1 : 0).ThenBy(f => f.Name).ToList();
        }
    }
}
