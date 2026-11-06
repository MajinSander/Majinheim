// Majinheim Mundo: mod propio que va SOLO en el server. Solo lee: no cambia nada del mundo ni de los jugadores.
// Cada pocos minutos apunta en BepInEx\majinheim\mundo.json qué hay en los cofres, los cajones y las tumbas, y dónde están
// los carteles y los portales; y una vez al arrancar, en recetas.json, las recetas y construcciones del juego con los mods,
// en español. El launcher del server lo lee para que el asistente de Discord conteste "¿dónde está el hierro?" o
// "¿cómo se hace X?" con datos de verdad.
// El launcher lo compila en el PC del server contra el Valheim instalado (así sigue funcionando cuando Valheim se actualiza).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using UnityEngine;

namespace MajinheimMundo
{
    [BepInPlugin("majinheim.mundo", "Majinheim Mundo", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        const float WorldEvery = 300f;         // segundos entre vistazos al mundo
        const int ZdosPerFrame = 40000;        // para no frenar el server: se repasa el mundo a trozos, un poco en cada fotograma
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        string outDir;
        bool busy, recipesDone;
        float busySince;
        float nextWorld = 90f;                 // el primer vistazo, al poco de cargar el mundo
        readonly Dictionary<string, string[]> names = new Dictionary<string, string[]>();   // prefab -> {español, inglés}
        HashSet<int> containerHashes, drawerHashes, signHashes, portalHashes, tombHashes;
        Dictionary<int, string> prefabNames;

        void Awake()
        {
            outDir = Path.Combine(Paths.BepInExRootPath, "majinheim");
            Directory.CreateDirectory(outDir);
            InvokeRepeating("Tick", 30f, 15f);
            Logger.LogInfo("Majinheim Mundo listo: solo lee el mundo y lo apunta en " + outDir);
        }

        bool Ready()
        {
            return ZNet.instance != null && ZNet.instance.IsServer() && ZDOMan.instance != null && ZNetScene.instance != null && ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0;
        }

        void Tick()
        {
            if (busy && Time.realtimeSinceStartup - busySince > 600f) busy = false;   // un vistazo que se quedó a medias
            if (busy || !Ready()) return;
            try
            {
                if (!chatHooked) HookChat();
                if (prefabNames == null) Classify();
                if (!recipesDone) { recipesDone = true; ExportRecipes(); }
            }
            catch (Exception e) { Logger.LogWarning("Majinheim Mundo: " + e); }
            if (Time.realtimeSinceStartup >= nextWorld)
            {
                nextWorld = Time.realtimeSinceStartup + WorldEvery;
                busy = true; busySince = Time.realtimeSinceStartup;
                StartCoroutine(ExportWorld());
            }
        }

        // ------------------------------------------------------------ chat de la partida: preguntas a MajinBot (!majin)
        // El server recibe todo lo que se escribe en el chat (va dirigido a todos). Solo se apuntan los mensajes que empiezan
        // por "!majin": el resto del chat no se guarda. El launcher los lee, pregunta a la IA y contesta en el chat por RCON.
        // Los tipos del mensaje cambian entre versiones de Valheim: se sacan de Chat.RPC_ChatMessage para no depender de ellos.

        bool chatHooked;

        void HookChat()
        {
            chatHooked = true;
            try
            {
                if (ZRoutedRpc.instance == null) { chatHooked = false; return; }
                var target = typeof(Chat).GetMethod("RPC_ChatMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (target == null) { Logger.LogWarning("Majinheim Mundo: no encuentro el chat de esta versión de Valheim; !majin no funcionará."); return; }
                var ps = target.GetParameters();
                if (ps.Length < 2 || ps[0].ParameterType != typeof(long) || ps.Length - 1 > 4) { Logger.LogWarning("Majinheim Mundo: el chat de esta versión es distinto; !majin no funcionará."); return; }
                var types = new Type[ps.Length - 1];
                for (int i = 1; i < ps.Length; i++) types[i - 1] = ps[i].ParameterType;
                var fns = typeof(ZRoutedRpc).GetField("m_functions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var dict = fns != null ? fns.GetValue(ZRoutedRpc.instance) as IDictionary : null;
                if (dict != null && dict.Contains(Hash("ChatMessage"))) { Logger.LogWarning("Majinheim Mundo: otro mod ya escucha el chat; !majin no funcionará."); return; }
                MethodInfo handler = null, register = null;
                foreach (var m in typeof(Plugin).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                    if (m.Name == "OnChat" && m.GetGenericArguments().Length == types.Length) handler = m.MakeGenericMethod(types);
                foreach (var m in typeof(ZRoutedRpc).GetMethods(BindingFlags.Instance | BindingFlags.Public))
                    if (m.Name == "Register" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == types.Length) register = m.MakeGenericMethod(types);
                if (handler == null || register == null) { Logger.LogWarning("Majinheim Mundo: no puedo escuchar el chat; !majin no funcionará."); return; }
                var actionType = register.GetParameters()[1].ParameterType;
                register.Invoke(ZRoutedRpc.instance, new object[] { "ChatMessage", Delegate.CreateDelegate(actionType, this, handler) });
                Logger.LogInfo("Majinheim Mundo: escuchando !majin en el chat de la partida.");
            }
            catch (Exception e) { Logger.LogWarning("Majinheim Mundo: no puedo escuchar el chat: " + e.Message); }
        }

        void OnChat<A>(long sender, A a) { OnChatLine(new object[] { a }); }
        void OnChat<A, B>(long sender, A a, B b) { OnChatLine(new object[] { a, b }); }
        void OnChat<A, B, C>(long sender, A a, B b, C c) { OnChatLine(new object[] { a, b, c }); }
        void OnChat<A, B, C, D>(long sender, A a, B b, C c, D d) { OnChatLine(new object[] { a, b, c, d }); }

        void OnChatLine(object[] args)
        {
            try
            {
                // El texto es el último string; el nombre, el primer string antes de él o el campo Name de UserInfo.
                int ti = -1;
                for (int i = args.Length - 1; i >= 0; i--) if (args[i] is string) { ti = i; break; }
                if (ti < 0) return;
                string text = ((string)args[ti]).Trim();
                if (!text.StartsWith("!majin", StringComparison.OrdinalIgnoreCase)) return;
                string name = null;
                for (int i = 0; i < ti && name == null; i++)
                {
                    if (args[i] is string) name = (string)args[i];
                    else if (args[i] != null && !(args[i] is int) && !(args[i] is Vector3))
                    {
                        var f = args[i].GetType().GetField("Name") ?? args[i].GetType().GetField("m_name");
                        if (f != null) name = f.GetValue(args[i]) as string;
                    }
                }
                string file = Path.Combine(outDir, "chat.jsonl");
                if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Delete(file);
                string line = "{\"t\":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ",\"nombre\":" + Q(name ?? "?") + ",\"texto\":" + Q(text.Length > 300 ? text.Substring(0, 300) : text) + "}\n";
                File.AppendAllText(file, line, new UTF8Encoding(false));
            }
            catch (Exception e) { Logger.LogWarning("Majinheim Mundo: chat: " + e.Message); }
        }

        // ------------------------------------------------------------ qué prefabs interesan

        void Classify()
        {
            prefabNames = new Dictionary<int, string>();
            containerHashes = new HashSet<int>(); drawerHashes = new HashSet<int>(); signHashes = new HashSet<int>();
            portalHashes = new HashSet<int>(); tombHashes = new HashSet<int>();
            var named = typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var dict = named != null ? named.GetValue(ZNetScene.instance) as Dictionary<int, GameObject> : null;
            var all = new List<KeyValuePair<int, GameObject>>();
            if (dict != null) all.AddRange(dict);
            else foreach (var go in ZNetScene.instance.m_prefabs) if (go != null) all.Add(new KeyValuePair<int, GameObject>(Hash(go.name), go));
            foreach (var kv in all)
            {
                var go = kv.Value;
                if (go == null) continue;
                string n = go.name;
                prefabNames[kv.Key] = n;
                var box = go.GetComponentInChildren<Container>(true);   // los barcos y los carros lo llevan dentro
                if (go.GetComponent<TombStone>() != null) tombHashes.Add(kv.Key);
                else if (box != null) { if (box.m_privacy.ToString() != "Private") containerHashes.Add(kv.Key); }   // los cofres personales no se miran
                else if (n.StartsWith("kg_ItemDrawer", StringComparison.OrdinalIgnoreCase)) drawerHashes.Add(kv.Key);
                else if (n == "sign" || n.StartsWith("sign_", StringComparison.OrdinalIgnoreCase)) signHashes.Add(kv.Key);
                else if (n.StartsWith("portal", StringComparison.OrdinalIgnoreCase)) portalHashes.Add(kv.Key);
            }
            Names();
            Logger.LogInfo("Majinheim Mundo: " + containerHashes.Count + " tipos de cofre, " + drawerHashes.Count + " de cajón, " + names.Count + " objetos.");
        }

        // El hash que usa Valheim para los nombres (GetStableHashCode).
        static int Hash(string s)
        {
            unchecked
            {
                int a = 5381, b = a;
                for (int i = 0; i < s.Length && s[i] != '\0'; i += 2)
                {
                    a = ((a << 5) + a) ^ s[i];
                    if (i == s.Length - 1 || s[i + 1] == '\0') break;
                    b = ((b << 5) + b) ^ s[i + 1];
                }
                return a + b * 1566083941;
            }
        }

        // ------------------------------------------------------------ nombres en español y en inglés

        void Names()
        {
            var tokens = new Dictionary<string, string>();   // prefab -> $token
            foreach (var go in ObjectDB.instance.m_items)
            {
                if (go == null) continue;
                var it = go.GetComponent<ItemDrop>();
                if (it != null && it.m_itemData != null && it.m_itemData.m_shared != null) tokens[go.name] = it.m_itemData.m_shared.m_name;
            }
            foreach (var kv in AllPieces())
            {
                var p = kv.Key.GetComponent<Piece>();
                if (p != null && !tokens.ContainsKey(kv.Key.name)) tokens[kv.Key.name] = p.m_name;
            }
            foreach (var st in Stations()) if (!tokens.ContainsKey(st.Key)) tokens[st.Key] = st.Value;
            var en = InLanguage("English", tokens);
            var es = InLanguage("Spanish", tokens);
            foreach (var kv in tokens)
            {
                string e, s;
                en.TryGetValue(kv.Key, out e); es.TryGetValue(kv.Key, out s);
                names[kv.Key] = new[] { s ?? e ?? kv.Key, e ?? s ?? kv.Key };
            }
        }

        // Traduce con el idioma pedido y deja el que hubiera. Valheim y los mods (Jotunn) cargan sus textos al cambiar de idioma.
        Dictionary<string, string> InLanguage(string lang, Dictionary<string, string> tokens)
        {
            var res = new Dictionary<string, string>();
            var loc = Localization.instance;
            if (loc == null) return res;
            var setup = typeof(Localization).GetMethod("SetupLanguage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            string prev = loc.GetSelectedLanguage();
            bool switched = false;
            try
            {
                if (prev != lang && setup != null) { setup.Invoke(loc, new object[] { lang }); switched = true; }
                foreach (var kv in tokens)
                {
                    string t = kv.Value ?? "";
                    if (!t.StartsWith("$")) { if (t.Length > 0) res[kv.Key] = t; continue; }
                    string l = loc.Localize(t);
                    if (!string.IsNullOrEmpty(l) && !l.StartsWith("$") && !l.StartsWith("[")) res[kv.Key] = l;
                }
            }
            catch (Exception e) { Logger.LogWarning("Majinheim Mundo: textos en " + lang + ": " + e.Message); }
            finally { if (switched) try { setup.Invoke(loc, new object[] { prev }); } catch { } }
            return res;
        }

        string Es(string prefab) { string[] n; return names.TryGetValue(prefab, out n) ? n[0] : prefab; }
        string En(string prefab) { string[] n; return names.TryGetValue(prefab, out n) ? n[1] : prefab; }

        // ------------------------------------------------------------ recetas y construcciones (una vez por arranque)

        // Todo lo que se construye, con la herramienta con la que se hace (martillo, azada, cultivador...).
        List<KeyValuePair<GameObject, string>> AllPieces()
        {
            var res = new List<KeyValuePair<GameObject, string>>();
            var seen = new HashSet<string>();
            foreach (var go in ObjectDB.instance.m_items)
            {
                if (go == null) continue;
                var it = go.GetComponent<ItemDrop>();
                if (it == null || it.m_itemData == null || it.m_itemData.m_shared == null) continue;
                var table = it.m_itemData.m_shared.m_buildPieces;
                if (table == null || table.m_pieces == null) continue;
                foreach (var p in table.m_pieces) if (p != null && seen.Add(p.name)) res.Add(new KeyValuePair<GameObject, string>(p, go.name));
            }
            return res;
        }

        // Estaciones de fabricación: prefab -> $token (para traducir "forja", "banco de trabajo"...).
        Dictionary<string, string> Stations()
        {
            var res = new Dictionary<string, string>();
            foreach (var r in ObjectDB.instance.m_recipes)
                if (r != null && r.m_craftingStation != null && !res.ContainsKey(r.m_craftingStation.name)) res[r.m_craftingStation.name] = r.m_craftingStation.m_name;
            foreach (var kv in AllPieces())
            {
                var p = kv.Key.GetComponent<Piece>();
                if (p != null && p.m_craftingStation != null && !res.ContainsKey(p.m_craftingStation.name)) res[p.m_craftingStation.name] = p.m_craftingStation.m_name;
            }
            return res;
        }

        void ExportRecipes()
        {
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"fecha\":").Append(Q(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv))).Append(",\"recetas\":[");
            bool first = true;
            foreach (var r in ObjectDB.instance.m_recipes)
            {
                if (r == null || r.m_item == null || !r.m_enabled || r.m_resources == null || r.m_resources.Length == 0) continue;
                string id = r.m_item.gameObject.name;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"id\":").Append(Q(id)).Append(",\"es\":").Append(Q(Es(id))).Append(",\"en\":").Append(Q(En(id)))
                  .Append(",\"cantidad\":").Append(r.m_amount);
                if (r.m_craftingStation != null)
                    sb.Append(",\"estacion\":").Append(Q(Es(r.m_craftingStation.name))).Append(",\"nivel\":").Append(r.m_minStationLevel);
                int maxQ = r.m_item.m_itemData != null && r.m_item.m_itemData.m_shared != null ? r.m_item.m_itemData.m_shared.m_maxQuality : 1;
                if (maxQ > 1) sb.Append(",\"mejorasHasta\":").Append(maxQ);
                if (r.m_requireOnlyOneIngredient) sb.Append(",\"soloUno\":true");
                Materials(sb, r.m_resources);
                sb.Append('}');
            }
            sb.Append("],\"construcciones\":[");
            first = true;
            foreach (var kv in AllPieces())
            {
                var go = kv.Key;
                var p = go.GetComponent<Piece>();
                if (p == null || !p.m_enabled || p.m_resources == null || p.m_resources.Length == 0) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"id\":").Append(Q(go.name)).Append(",\"es\":").Append(Q(Es(go.name))).Append(",\"en\":").Append(Q(En(go.name)))
                  .Append(",\"herramienta\":").Append(Q(Es(kv.Value)));
                if (p.m_craftingStation != null) sb.Append(",\"estacion\":").Append(Q(Es(p.m_craftingStation.name)));
                Materials(sb, p.m_resources);
                sb.Append('}');
            }
            sb.Append("],\"nombres\":{");   // todo lo que existe, para saber que algo existe aunque no esté en ningún cofre
            first = true;
            foreach (var kv in names)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append(Q(kv.Key)).Append(":[").Append(Q(kv.Value[0])).Append(',').Append(Q(kv.Value[1])).Append(']');
            }
            sb.Append("}}");
            Save("recetas.json", sb.ToString());
        }

        void Materials(StringBuilder sb, Piece.Requirement[] reqs)
        {
            sb.Append(",\"materiales\":[");
            bool first = true;
            foreach (var q in reqs)
            {
                if (q == null || q.m_resItem == null) continue;
                string id = q.m_resItem.gameObject.name;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"id\":").Append(Q(id)).Append(",\"es\":").Append(Q(Es(id))).Append(",\"n\":").Append(q.m_amount);
                if (q.m_amountPerLevel > 0) sb.Append(",\"porMejora\":").Append(q.m_amountPerLevel);
                sb.Append('}');
            }
            sb.Append(']');
        }

        // ------------------------------------------------------------ el mundo: cofres, cajones, tumbas, carteles y portales

        IEnumerator ExportWorld()
        {
            var t0 = Time.realtimeSinceStartup;
            List<ZDO> zdos = null;
            try
            {
                var f = typeof(ZDOMan).GetField("m_objectsByID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var dict = f != null ? f.GetValue(ZDOMan.instance) as Dictionary<ZDOID, ZDO> : null;
                if (dict != null) zdos = new List<ZDO>(dict.Values);
            }
            catch (Exception e) { Logger.LogWarning("Majinheim Mundo: no puedo leer el mundo: " + e.Message); }
            if (zdos == null) { busy = false; yield break; }

            var chests = new StringBuilder(1 << 18); var drawers = new StringBuilder(1 << 16); var tombs = new StringBuilder(1 << 12);
            var signs = new StringBuilder(1 << 14); var portals = new StringBuilder(1 << 12);
            int nChests = 0, nDrawers = 0, nTombs = 0, nSigns = 0, nPortals = 0, nBad = 0;
            string firstBad = null;
            var used = new HashSet<string>();
            for (int i = 0; i < zdos.Count; i++)
            {
                if (i > 0 && i % ZdosPerFrame == 0) yield return null;
                var z = zdos[i];
                if (z == null || !z.IsValid()) continue;
                int h = z.GetPrefab();
                try
                {
                    if (containerHashes.Contains(h) || tombHashes.Contains(h))
                    {
                        bool tomb = tombHashes.Contains(h);
                        string items = z.GetString("items", "");
                        var list = ReadItems(items);
                        if (!tomb && list.Count == 0) continue;
                        var sb = tomb ? tombs : chests;
                        if ((tomb ? nTombs : nChests) > 0) sb.Append(',');
                        if (tomb) nTombs++; else nChests++;
                        Pos(sb.Append('{'), z);
                        string pn; prefabNames.TryGetValue(h, out pn);
                        sb.Append(",\"tipo\":").Append(Q(pn ?? "")).Append(",\"nombre\":").Append(Q(Es(pn ?? "")));
                        if (tomb)
                        {
                            sb.Append(",\"jugador\":").Append(Q(z.GetString("ownerName", "")));
                            long death = z.GetLong("timeOfDeath", 0L);
                            if (death > 0) sb.Append(",\"muerte\":").Append(Q(new DateTime(death).ToString("yyyy-MM-ddTHH:mm:ss", Inv)));
                        }
                        sb.Append(",\"objetos\":[");
                        for (int k = 0; k < list.Count; k++)
                        {
                            if (k > 0) sb.Append(',');
                            sb.Append("{\"id\":").Append(Q(list[k].Id)).Append(",\"n\":").Append(list[k].Stack);
                            if (list[k].Quality > 1) sb.Append(",\"calidad\":").Append(list[k].Quality);
                            sb.Append('}');
                            used.Add(list[k].Id);
                        }
                        sb.Append("]}");
                    }
                    else if (drawerHashes.Contains(h))
                    {
                        string item = z.GetString("Prefab", "");
                        int amount = z.GetInt("Amount", 0);
                        if (item.Length == 0 || amount <= 0) continue;
                        if (nDrawers++ > 0) drawers.Append(',');
                        Pos(drawers.Append('{'), z);
                        drawers.Append(",\"id\":").Append(Q(item)).Append(",\"n\":").Append(amount).Append('}');
                        used.Add(item);
                    }
                    else if (signHashes.Contains(h))
                    {
                        string text = z.GetString("text", "").Trim();
                        if (text.Length == 0) continue;
                        if (nSigns++ > 0) signs.Append(',');
                        Pos(signs.Append('{'), z);
                        signs.Append(",\"texto\":").Append(Q(text.Length > 80 ? text.Substring(0, 80) : text)).Append('}');
                    }
                    else if (portalHashes.Contains(h))
                    {
                        if (nPortals++ > 0) portals.Append(',');
                        Pos(portals.Append('{'), z);
                        portals.Append(",\"nombre\":").Append(Q(z.GetString("tag", "").Trim())).Append('}');
                    }
                }
                catch (Exception e) { if (nBad++ == 0) firstBad = z.m_uid + ": " + e.Message; }   // uno raro no para el resto
            }

            var all = new StringBuilder(chests.Length + drawers.Length + 4096);
            int day = EnvMan.instance != null ? EnvMan.instance.GetCurrentDay() : 0;
            all.Append("{\"fecha\":").Append(Q(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv))).Append(",\"dia\":").Append(day)
               .Append(",\"cofres\":[").Append(chests).Append("],\"cajones\":[").Append(drawers).Append("],\"tumbas\":[").Append(tombs)
               .Append("],\"carteles\":[").Append(signs).Append("],\"portales\":[").Append(portals).Append("],\"nombres\":{");
            bool first = true;
            foreach (var id in used)
            {
                if (!first) all.Append(','); first = false;
                all.Append(Q(id)).Append(":[").Append(Q(Es(id))).Append(',').Append(Q(En(id))).Append(']');
            }
            all.Append("}}");
            Save("mundo.json", all.ToString());
            if (nBad > 0) Logger.LogWarning("Majinheim Mundo: " + nBad + " objetos no se han podido leer (el primero, " + firstBad + ").");
            Logger.LogInfo(string.Format(Inv, "Majinheim Mundo: {0} cofres, {1} cajones, {2} tumbas, {3} carteles y {4} portales ({5:0.0} s, {6} objetos del mundo).",
                nChests, nDrawers, nTombs, nSigns, nPortals, Time.realtimeSinceStartup - t0, zdos.Count));
            busy = false;
        }

        struct Item { public string Id; public int Stack, Quality; }

        // Lo que hay dentro de un cofre, tal como lo guarda Valheim (Inventory.Save), sin crear nada en el mundo.
        static List<Item> ReadItems(string b64)
        {
            var res = new List<Item>();
            if (string.IsNullOrEmpty(b64)) return res;
            var p = new ZPackage(b64);
            int ver = p.ReadInt(), count = p.ReadInt();
            if (ver < 100 || ver > 106 || count < 0 || count > 1000) return res;   // formato que no conozco: mejor no inventar
            for (int i = 0; i < count; i++)
            {
                var it = new Item();
                it.Id = p.ReadString();
                it.Stack = p.ReadInt();
                p.ReadSingle();                 // durabilidad
                p.ReadInt(); p.ReadInt();       // posición en el cofre
                p.ReadBool();                   // equipado
                it.Quality = ver >= 101 ? p.ReadInt() : 1;
                if (ver >= 102) p.ReadInt();    // variante
                if (ver >= 103) { p.ReadLong(); p.ReadString(); }   // quién lo fabricó
                if (ver >= 104) { int n = p.ReadInt(); for (int k = 0; k < n; k++) { p.ReadString(); p.ReadString(); } }
                if (ver >= 105) p.ReadInt();    // nivel del mundo
                if (ver >= 106) p.ReadBool();   // recogido
                if (!string.IsNullOrEmpty(it.Id) && it.Stack > 0) res.Add(it);
            }
            return res;
        }

        static StringBuilder Pos(StringBuilder sb, ZDO z)
        {
            var v = z.GetPosition();
            return sb.Append("\"x\":").Append(Mathf.RoundToInt(v.x)).Append(",\"y\":").Append(Mathf.RoundToInt(v.y)).Append(",\"z\":").Append(Mathf.RoundToInt(v.z));
        }

        static string Q(string s)
        {
            s = s ?? "";
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // Se escribe fuera del hilo del juego (y primero a un temporal, para que el launcher nunca lea un archivo a medias).
        void Save(string name, string json)
        {
            string path = Path.Combine(outDir, name);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (Exception e) { Logger.LogWarning("Majinheim Mundo: no pude guardar " + name + ": " + e.Message); }
            });
        }
    }
}
