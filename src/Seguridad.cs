// Fase 5, "Calidad por dentro" (2.5):
//  - Verificación en dos pasos de la web: códigos de 6 cifras que cambian cada 30 segundos (TOTP, RFC 6238, como
//    Google Authenticator o Microsoft Authenticator) y el código QR para darlos de alta escaneándolo. Sin librerías de fuera.
//  - Dispositivos conocidos de cada administrador, para avisar en Discord cuando entra desde uno nuevo.
//  - Ajustes de los mods desde la web (con copia del archivo antes de tocarlo).
// Compilado con el csc de .NET Framework 4.8 (C# 5).
#if SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Majinheim
{
    public static class Totp
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        static readonly Dictionary<string, long> lastStep = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // Clave nueva (20 bytes al azar) en base32, que es como la piden las apps.
        public static string NewSecret()
        {
            var b = new byte[20];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Base32(b);
        }

        public static string Base32(byte[] data)
        {
            var sb = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (byte x in data)
            {
                buffer = (buffer << 8) | x; bits += 8;
                while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] FromBase32(string s)
        {
            var o = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (char c in (s ?? "").ToUpperInvariant())
            {
                int v = Alphabet.IndexOf(c);
                if (v < 0) continue;   // espacios y guiones
                buffer = (buffer << 5) | v; bits += 5;
                if (bits >= 8) { o.Add((byte)((buffer >> (bits - 8)) & 255)); bits -= 8; }
            }
            return o.ToArray();
        }

        // Para escribirla a mano: en grupos de 4.
        public static string Grouped(string secret)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < secret.Length; i++) { if (i > 0 && i % 4 == 0) sb.Append(' '); sb.Append(secret[i]); }
            return sb.ToString();
        }

        public static long Step(DateTime utc) { return (long)Math.Floor((utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds / 30); }

        public static string Code(byte[] key, long step, int digits = 6)
        {
            var msg = new byte[8];
            for (int i = 7; i >= 0; i--) { msg[i] = (byte)(step & 0xFF); step >>= 8; }
            byte[] h;
            using (var hmac = new HMACSHA1(key)) h = hmac.ComputeHash(msg);
            int off = h[h.Length - 1] & 0x0F;
            int bin = ((h[off] & 0x7F) << 24) | (h[off + 1] << 16) | (h[off + 2] << 8) | h[off + 3];
            int mod = 1; for (int i = 0; i < digits; i++) mod *= 10;
            return (bin % mod).ToString(new string('0', digits));
        }

        // Vale el código de ahora, el de antes y el de después (por si el reloj del móvil va un poco desfasado),
        // y cada código solo sirve una vez para la misma cuenta.
        public static bool Check(string secret, string code, string account)
        {
            code = new string((code ?? "").Where(char.IsDigit).ToArray());
            if (code.Length != 6) return false;
            var key = FromBase32(secret);
            if (key.Length < 10) return false;
            long now = Step(DateTime.UtcNow);
            for (long s = now - 1; s <= now + 1; s++)
            {
                if (!Same(Code(key, s), code)) continue;
                lock (lastStep)
                {
                    long last;
                    if (lastStep.TryGetValue(account ?? "", out last) && s <= last) return false;
                    lastStep[account ?? ""] = s;
                }
                return true;
            }
            return false;
        }

        static bool Same(string a, string b)
        {
            int d = a.Length ^ b.Length;
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        public static string Uri(string account, string secret)
        {
            return "otpauth://totp/" + System.Uri.EscapeDataString("Majinheim:" + account) + "?secret=" + secret + "&issuer=Majinheim&algorithm=SHA1&digits=6&period=30";
        }
    }

    // Código QR (modo bytes, corrección de errores M, versiones 1 a 10). Sigue el estándar ISO/IEC 18004.
    public static class QrCode
    {
        static readonly int[] EccPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };   // nivel M
        static readonly int[] Blocks = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

        static int RawModules(int ver)
        {
            int r = (16 * ver + 128) * ver + 64;
            if (ver >= 2) { int n = ver / 7 + 2; r -= (25 * n - 10) * n - 55; if (ver >= 7) r -= 36; }
            return r;
        }
        static int DataCodewords(int ver) { return RawModules(ver) / 8 - EccPerBlock[ver] * Blocks[ver]; }

        public static bool[,] Encode(string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            int ver = 1;
            while (ver <= 10 && 4 + (ver < 10 ? 8 : 16) + data.Length * 8 > DataCodewords(ver) * 8) ver++;
            if (ver > 10) throw new ArgumentException("Texto demasiado largo para el QR");

            // Bits: modo bytes, longitud, datos, terminador y relleno.
            var bits = new List<bool>();
            Action<int, int> add = (v, n) => { for (int i = n - 1; i >= 0; i--) bits.Add(((v >> i) & 1) != 0); };
            add(4, 4);
            add(data.Length, ver < 10 ? 8 : 16);
            foreach (var b in data) add(b, 8);
            int cap = DataCodewords(ver) * 8;
            add(0, Math.Min(4, cap - bits.Count));
            add(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) add(pad, 8);
            var cw = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(0x80 >> (i & 7));

            var all = Interleave(cw, ver);
            int size = ver * 4 + 17;
            var m = new bool[size, size];      // [y, x]
            var fn = new bool[size, size];
            DrawFunctions(m, fn, ver, 0);
            DrawCodewords(m, fn, all);

            // La máscara con menos penalización (la que mejor se lee).
            int best = 0; long bestScore = long.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(m, fn, mask);
                DrawFormat(m, fn, mask);
                long sc = Penalty(m);
                if (sc < bestScore) { bestScore = sc; best = mask; }
                ApplyMask(m, fn, mask);
            }
            ApplyMask(m, fn, best);
            DrawFormat(m, fn, best);
            return m;
        }

        static byte[] Interleave(byte[] data, int ver)
        {
            int nb = Blocks[ver], ecc = EccPerBlock[ver], raw = RawModules(ver) / 8;
            int shortBlocks = nb - raw % nb, shortLen = raw / nb;
            var div = RsDivisor(ecc);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < nb; i++)
            {
                int len = shortLen - ecc + (i < shortBlocks ? 0 : 1);
                var dat = data.Skip(k).Take(len).ToArray();
                k += len;
                var blk = new byte[shortLen + 1];
                Array.Copy(dat, blk, dat.Length);
                var rem = RsRemainder(dat, div);
                Array.Copy(rem, 0, blk, shortLen + 1 - ecc, ecc);
                blocks.Add(blk);
            }
            var o = new List<byte>();
            for (int i = 0; i < shortLen + 1; i++)
                for (int j = 0; j < nb; j++)
                    if (i != shortLen - ecc || j >= shortBlocks) o.Add(blocks[j][i]);
            return o.ToArray();
        }

        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
            return z & 0xFF;
        }
        static byte[] RsDivisor(int degree)
        {
            var r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++) { r[j] = (byte)Mul(r[j], root); if (j + 1 < degree) r[j] ^= r[j + 1]; }
                root = Mul(root, 0x02);
            }
            return r;
        }
        static byte[] RsRemainder(byte[] data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (byte b in data)
            {
                int f = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], f);
            }
            return r;
        }

        static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark) { m[y, x] = dark; fn[y, x] = true; }

        static void DrawFunctions(bool[,] m, bool[,] fn, int ver, int mask)
        {
            int size = m.GetLength(0);
            for (int i = 0; i < size; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }
            foreach (var c in new[] { new[] { 3, 3 }, new[] { size - 4, 3 }, new[] { 3, size - 4 } })
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = c[0] + dx, y = c[1] + dy;
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        if (x >= 0 && x < size && y >= 0 && y < size) Set(m, fn, x, y, d != 2 && d != 4);
                    }
            var pos = Align(ver);
            for (int i = 0; i < pos.Count; i++)
                for (int j = 0; j < pos.Count; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == pos.Count - 1) || (i == pos.Count - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++) Set(m, fn, pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormat(m, fn, mask);
            if (ver >= 7)
            {
                int rem = ver;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = ver << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + i % 3, b = i / 3;
                    Set(m, fn, a, b, bit); Set(m, fn, b, a, bit);
                }
            }
        }

        static List<int> Align(int ver)
        {
            var r = new List<int>();
            if (ver == 1) return r;
            int n = ver / 7 + 2, size = ver * 4 + 17;
            int step = (ver * 4 + n * 2 + 1) / (n * 2 - 2) * 2;
            r.Add(6);
            for (int i = 0, p = size - 7; i < n - 1; i++, p -= step) r.Insert(1, p);
            return r;
        }

        static void DrawFormat(bool[,] m, bool[,] fn, int mask)
        {
            int size = m.GetLength(0);
            int data = 0 << 3 | mask;   // nivel M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            Func<int, bool> bit = i => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(m, fn, 8, i, bit(i));
            Set(m, fn, 8, 7, bit(6)); Set(m, fn, 8, 8, bit(7)); Set(m, fn, 7, 8, bit(8));
            for (int i = 9; i < 15; i++) Set(m, fn, 14 - i, 8, bit(i));
            for (int i = 0; i < 8; i++) Set(m, fn, size - 1 - i, 8, bit(i));
            for (int i = 8; i < 15; i++) Set(m, fn, 8, size - 15 + i, bit(i));
            Set(m, fn, 8, size - 8, true);
        }

        static void DrawCodewords(bool[,] m, bool[,] fn, byte[] data)
        {
            int size = m.GetLength(0), i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int v = 0; v < size; v++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool up = ((right + 1) & 2) == 0;
                        int y = up ? size - 1 - v : v;
                        if (!fn[y, x] && i < data.Length * 8) { m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0; i++; }
                    }
            }
        }

        static void ApplyMask(bool[,] m, bool[,] fn, int mask)
        {
            int size = m.GetLength(0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (fn[y, x]) continue;
                    bool inv;
                    switch (mask)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) m[y, x] = !m[y, x];
                }
        }

        // Penalización del estándar: filas o columnas largas del mismo color, bloques 2x2, patrones parecidos a las esquinas y desequilibrio.
        static long Penalty(bool[,] m)
        {
            int size = m.GetLength(0);
            long p = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    var line = new bool[size];
                    for (int b = 0; b < size; b++) line[b] = pass == 0 ? m[a, b] : m[b, a];
                    for (int b = 1; b < size; b++)
                    {
                        if (line[b] == line[b - 1]) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                        else run = 1;
                    }
                    for (int b = 0; b + 7 <= size; b++)
                    {
                        if (!(line[b] && !line[b + 1] && line[b + 2] && line[b + 3] && line[b + 4] && !line[b + 5] && line[b + 6])) continue;
                        bool before = true, after = true;
                        for (int k = 1; k <= 4; k++) { if (b - k >= 0 && line[b - k]) before = false; if (b + 6 + k < size && line[b + 6 + k]) after = false; }
                        if (before || after) p += 40;
                    }
                }
            int dark = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x + 1 < size && y + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) p += 3;
                }
            int total = size * size;
            int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            p += Math.Max(0, k2) * 10;
            return p;
        }

        // SVG con 4 módulos de margen blanco (lo que piden los lectores).
        public static string Svg(bool[,] m)
        {
            int size = m.GetLength(0), border = 4, n = size + border * 2;
            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(n).Append(' ').Append(n).Append("\" shape-rendering=\"crispEdges\">");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (m[y, x]) sb.Append('M').Append(x + border).Append(',').Append(y + border).Append("h1v1h-1z");
            sb.Append("\"/></svg>");
            return sb.ToString();
        }

        public static string SvgDataUri(string text)
        {
            return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Svg(Encode(text))));
        }
    }

    // Lo que contesta el launcher cuando la web le pide algo que tarda o puede salir mal (restaurar, ajustes de mods).
    public class WebResult
    {
        public bool Ok; public string Message;
        public static WebResult Yes(string m) { return new WebResult { Ok = true, Message = m }; }
        public static WebResult No(string m) { return new WebResult { Ok = false, Message = m }; }
    }

    // Dispositivos desde los que ha entrado cada administrador: solo la huella SHA-256 de una cookie propia del navegador
    // (nunca la IP). Si entra desde uno que no está, se avisa en Discord.
    public static class WebDevices
    {
        public class Entry
        {
            public string User { get; set; }
            public string Hash { get; set; }
            public string Name { get; set; }
            public DateTime First { get; set; }
            public DateTime Last { get; set; }
        }
        const int KeepPerUser = 20;
        static readonly object lk = new object();
        static string FileName { get { return AppInfo.P("web", "dispositivos.json"); } }

        static List<Entry> Load()
        {
            try { return (Json.Load<List<Entry>>(FileName) ?? new List<Entry>()).Where(x => x != null && !string.IsNullOrEmpty(x.Hash)).ToList(); }
            catch (Exception e) { Log.W("Web: no pude leer los dispositivos conocidos: {0}", e.Message); return new List<Entry>(); }
        }

        // true si ya había entrado desde ahí; si no, lo apunta. Se quedan los 20 últimos de cada cuenta.
        public static bool Seen(string user, string hash, string name)
        {
            lock (lk)
            {
                var all = Load();
                var e = all.FirstOrDefault(x => x.Hash == hash && string.Equals(x.User, user, StringComparison.OrdinalIgnoreCase));
                bool known = e != null;
                if (e == null) { e = new Entry { User = user, Hash = hash, First = DateTime.UtcNow }; all.Add(e); }
                e.Name = name; e.Last = DateTime.UtcNow;
                foreach (var old in all.Where(x => string.Equals(x.User, user, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Last).Skip(KeepPerUser).ToList()) all.Remove(old);
                try { Json.Save(FileName, all); } catch (Exception ex) { Log.W("Web: no pude guardar los dispositivos conocidos: {0}", ex.Message); }
                return known;
            }
        }

        public static void Forget(string user)
        {
            lock (lk)
            {
                var all = Load();
                if (all.RemoveAll(x => string.Equals(x.User, user, StringComparison.OrdinalIgnoreCase)) == 0) return;
                try { Json.Save(FileName, all); } catch { }
            }
        }
    }

    // Ajustes de los mods (los .cfg de BepInEx del server) para la web de administración.
    public static class ModSettings
    {
        // (archivo, ajuste) que se enseñan en "Los más usados", en la web y en el launcher
        public static readonly string[][] FavoriteKeys =
        {
            new[] { "kg.ItemDrawers", "DrawerPickupRange" },
            new[] { "kg.ItemDrawers", "MaxDrawerPickupRange" },
            new[] { "Azumatt.AzuExtendedPlayerInventory", "Extra Inventory Rows" },
            new[] { "Azumatt.AzuExtendedPlayerInventory", "Slots Per Row" },
            new[] { "Azumatt.AzuCraftyBoxes", "Container Range" },
            new[] { "kg.ItemDrawers", "Radius" },
        };
        public const int MaxValue = 500, MaxChanges = 40;

        public static string Dir { get { return Path.Combine(AppInfo.Profile, @"BepInEx\config"); } }

        public class Change { public string File, Section, Key, Value; }

        public static List<Dictionary<string, object>> Files()
        {
            return CfgFile.LoadAll(Dir).Select(f => new Dictionary<string, object>
            {
                { "archivo", Path.GetFileName(f.Path) }, { "nombre", f.Name }, { "ajustes", f.Entries.Count },
                { "distintos", f.Entries.Count(e => e.Default != null && !e.IsDefault) },
            }).ToList();
        }

        public static IEnumerable<CfgEntry> Favorites(List<CfgFile> all)
        {
            return FavoriteKeys.Select(k => all.Where(x => Path.GetFileNameWithoutExtension(x.Path).Equals(k[0], StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Entries).FirstOrDefault(e => e.Key == k[1])).Where(e => e != null);
        }

        // file: uno concreto; query: buscar en todos; los dos vacíos: los más usados.
        public static Dictionary<string, object> List(string file, string query)
        {
            var all = CfgFile.LoadAll(Dir);
            IEnumerable<CfgEntry> rows;
            string name = "Los más usados";
            query = (query ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(file))
            {
                var f = all.FirstOrDefault(x => string.Equals(Path.GetFileName(x.Path), file, StringComparison.OrdinalIgnoreCase));
                if (f == null) return new Dictionary<string, object> { { "ok", false }, { "mensaje", "Ese mod ya no tiene ajustes." } };
                rows = f.Entries; name = f.Name;
            }
            else if (query.Length > 0) { rows = all.SelectMany(x => x.Entries); name = "Buscando «" + query + "»"; }
            else rows = Favorites(all);
            if (query.Length > 0) rows = rows.Where(e => (e.Key + " " + e.Section + " " + e.Description + " " + e.File.Name).ToLowerInvariant().Contains(query));
            var list = rows.Take(150).Select(Row).ToList();
            return new Dictionary<string, object> { { "ok", true }, { "titulo", name }, { "ajustes", list } };
        }

        static Dictionary<string, object> Row(CfgEntry e)
        {
            return new Dictionary<string, object>
            {
                { "archivo", Path.GetFileName(e.File.Path) }, { "mod", e.File.Name }, { "seccion", e.Section }, { "clave", e.Key }, { "valor", e.Value },
                { "defecto", e.Default }, { "tipo", e.Type }, { "descripcion", e.Description }, { "rango", e.Range != null ? e.Range.Replace("From", "de").Replace(" to ", " a ") : null },
                { "opciones", e.Acceptable }, { "varios", e.Multi },
            };
        }

        static string Clean(string v)
        {
            v = System.Text.RegularExpressions.Regex.Replace(v ?? "", @"[\x00-\x1f\x7f]+", " ").Trim();   // ni saltos de línea ni nada que meta líneas nuevas en el .cfg
            return v;
        }

        // Comprueba todo, hace la copia de los archivos que cambian y los guarda. No toca nada si algo no vale.
        public static WebResult Apply(List<Change> changes, string who)
        {
            if (changes == null || changes.Count == 0) return WebResult.No("No hay cambios.");
            if (changes.Count > MaxChanges) return WebResult.No("Demasiados cambios de una vez (máximo " + MaxChanges + ").");
            var all = CfgFile.LoadAll(Dir);
            var done = new List<string>();
            var touched = new List<CfgFile>();
            foreach (var c in changes)
            {
                var f = all.FirstOrDefault(x => string.Equals(Path.GetFileName(x.Path), c.File, StringComparison.OrdinalIgnoreCase));
                if (f == null) return WebResult.No("No encuentro los ajustes de " + (c.File ?? "ese mod") + ".");
                var e = f.Entries.FirstOrDefault(x => x.Section == c.Section && x.Key == c.Key);
                if (e == null) return WebResult.No("No encuentro el ajuste «" + c.Key + "» en " + f.Name + ".");
                string v = Clean(c.Value);
                if (v.Length > MaxValue) return WebResult.No("«" + e.Key + "»: el valor es demasiado largo.");
                if (v == e.Value) continue;
                string before = e.Value;
                e.Value = v;
                string err = e.Validate();
                if (err != null) return WebResult.No("«" + e.Key + "» (" + f.Name + ") " + err + ".");
                done.Add(f.Name + " › " + e.Key + ": " + before + " → " + v);
                if (!touched.Contains(f)) touched.Add(f);
            }
            if (done.Count == 0) return WebResult.No("No hay nada que cambiar: ya tenían esos valores.");
            Backup.Create("ajustes-mods", touched.Select(f => new Backup.Item(f.Path, "cfg_" + Path.GetFileNameWithoutExtension(f.Path))), true);
            foreach (var f in touched) f.Save();
            foreach (var d in done) Log.W("Ajuste cambiado desde la web ({0}): {1}", who, d);
            return WebResult.Yes((done.Count == 1 ? "Guardado: " + done[0] : done.Count + " ajustes guardados") + ". Hay copia de cómo estaba. La mayoría de mods lo aplican al momento, también a los que están jugando; si alguno no lo nota, se aplica al reiniciar el server.");
        }
    }
}
#endif
