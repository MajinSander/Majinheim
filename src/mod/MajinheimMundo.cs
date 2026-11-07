// Majinheim Mundo: mod propio que va SOLO en el server. Solo lee: no cambia nada del mundo ni de los jugadores.
// Cada pocos minutos apunta en BepInEx\majinheim\mundo.json qué hay en los cofres, los cajones y las tumbas, y dónde están
// los carteles y los portales; cada 15 s, en rendimiento.json, a cuántos fotogramas por segundo va el server (para el
// aviso de lag); en mapa.bin, el terreno de lo explorado (para el mapa de "Buscar en el mundo" de la web);
// y una vez al arrancar, en recetas.json, las recetas y construcciones del juego con los mods,
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
using HarmonyLib;
using UnityEngine;

namespace MajinheimMundo
{
    [BepInPlugin("majinheim.mundo", "Majinheim Mundo", "1.3.1")]
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

        // ------------------------------------------------------------ rendimiento: fotogramas por segundo del server
        // Un server de Valheim sano pasa de 30 fotogramas por segundo; cuando no da abasto baja, y los jugadores lo notan
        // como lag (enemigos que se teletransportan, puertas que tardan en abrirse...).
        int frames; float worst, perfSince = -1f;

        void Update()
        {
            frames++;
            float dt = Time.unscaledDeltaTime;
            if (dt > worst) worst = dt;
        }

        FieldInfo zdoField;

        void SavePerf()
        {
            float now = Time.realtimeSinceStartup;
            if (perfSince < 0f) { perfSince = now; frames = 0; worst = 0f; return; }
            float span = now - perfSince;
            if (span < 5f) return;
            int zdos = -1;
            try
            {
                if (zdoField == null) zdoField = typeof(ZDOMan).GetField("m_objectsByID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var c = zdoField != null && ZDOMan.instance != null ? zdoField.GetValue(ZDOMan.instance) as ICollection : null;
                if (c != null) zdos = c.Count;
            }
            catch { }
            string json = "{\"t\":" + (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds +
                ",\"fps\":" + (frames / span).ToString("0.0", Inv) + ",\"peor\":" + ((int)(worst * 1000f)).ToString(Inv) +
                ",\"objetos\":" + zdos.ToString(Inv) + "}";
            perfSince = now; frames = 0; worst = 0f;
            Save("rendimiento.json", json);
        }

        void Tick()
        {
            if (busy && Time.realtimeSinceStartup - busySince > 600f) busy = false;   // un vistazo que se quedó a medias
            if (Ready()) try { SavePerf(); } catch (Exception e) { Logger.LogWarning("Majinheim Mundo: rendimiento: " + e.Message); }
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
            else if (!mapBusy && Time.realtimeSinceStartup >= nextMap)
            {
                nextMap = Time.realtimeSinceStartup + WorldEvery;
                mapBusy = true;
                StartCoroutine(ExportMap());
            }
        }

        // ------------------------------------------------------------ terreno de lo explorado (mapa.bin)
        // Las zonas (64 x 64 m) que el juego ya ha generado son las que alguien ha tenido cerca: eso es "lo explorado".
        // De cada una se apunta el bioma y la altura cada 16 m, calculados con el generador del mundo (no se toca nada).
        // Solo se vuelve a escribir si hay zonas nuevas. Por reflexión, para que un cambio de Valheim no rompa el resto del mod.
        const int MapStep = 16, MapPer = 4;    // 4 x 4 muestras por zona
        const int ZonesPerFrame = 150;
        bool mapBusy, mapBroken;
        float nextMap = 150f;
        int mapZones = -1;

        static MethodInfo Sampler(string name)
        {
            foreach (var m in typeof(WorldGenerator).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length < 2 || ps[0].ParameterType != typeof(float) || ps[1].ParameterType != typeof(float)) continue;
                bool ok = true;
                for (int i = 2; i < ps.Length; i++) if (ps[i].IsOut || ps[i].ParameterType.IsByRef || !ps[i].IsOptional) ok = false;
                if (ok) return m;
            }
            return null;
        }

        static object[] Args(MethodInfo m)
        {
            var ps = m.GetParameters(); var a = new object[ps.Length];
            for (int i = 2; i < ps.Length; i++) a[i] = ps[i].DefaultValue;
            return a;
        }

        static int BiomeIndex(int flag)
        {
            if (flag <= 0) return 0;
            int i = 1; while ((flag & 1) == 0) { flag >>= 1; i++; }
            return i;   // 1 Pradera, 2 Pantano, 3 Montaña, 4 Selva Negra, 5 Llanuras, 6 Tierras de ceniza, 7 Norte profundo, 9 Océano, 10 Tierras de niebla
        }

        IEnumerator ExportMap()
        {
            var zones = new List<int>();
            MethodInfo biome = null, height = null;
            try
            {
                if (mapBroken || ZoneSystem.instance == null || WorldGenerator.instance == null) { mapBusy = false; yield break; }
                var f = typeof(ZoneSystem).GetField("m_generatedZones", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var set = f != null ? f.GetValue(ZoneSystem.instance) as IEnumerable : null;
                biome = Sampler("GetBiome"); height = Sampler("GetHeight");
                if (set == null || biome == null || height == null)
                {
                    mapBroken = true;
                    Logger.LogWarning("Majinheim Mundo: esta versión de Valheim no deja leer el terreno; el mapa de la web no tendrá fondo.");
                    mapBusy = false; yield break;
                }
                FieldInfo fx = null, fy = null;
                foreach (var v in set)
                {
                    if (fx == null) { fx = v.GetType().GetField("x"); fy = v.GetType().GetField("y"); }
                    if (fx == null || fy == null) break;
                    zones.Add(Convert.ToInt32(fx.GetValue(v))); zones.Add(Convert.ToInt32(fy.GetValue(v)));   // en el juego son short
                }
            }
            catch (Exception e) { Logger.LogWarning("Majinheim Mundo: no puedo leer las zonas: " + e.Message); mapBroken = true; mapBusy = false; yield break; }
            int n = zones.Count / 2;
            if (n == 0 || n == mapZones) { mapBusy = false; yield break; }

            var data = new byte[12 + n * (4 + MapPer * MapPer * 2)];
            var bw = new BinaryWriter(new MemoryStream(data));
            bw.Write(Encoding.ASCII.GetBytes("MHM1")); bw.Write(n); bw.Write((short)MapStep); bw.Write((short)MapPer);
            var ab = Args(biome); var ah = Args(height);
            var gen = WorldGenerator.instance;
            for (int k = 0; k < n; k++)
            {
                if (k > 0 && k % ZonesPerFrame == 0) yield return null;
                int zx = zones[k * 2], zy = zones[k * 2 + 1];
                bw.Write((short)zx); bw.Write((short)zy);
                for (int j = 0; j < MapPer; j++)
                    for (int i = 0; i < MapPer; i++)
                    {
                        float x = zx * 64f - 32f + MapStep / 2f + i * MapStep, z = zy * 64f - 32f + MapStep / 2f + j * MapStep;
                        int b = 0; float h = 0f;
                        try
                        {
                            ab[0] = x; ab[1] = z; b = BiomeIndex(Convert.ToInt32(biome.Invoke(gen, ab)));
                            ah[0] = x; ah[1] = z; h = Convert.ToSingle(height.Invoke(gen, ah));
                        }
                        catch (Exception e)
                        {
                            Logger.LogWarning("Majinheim Mundo: no puedo leer el terreno: " + (e.InnerException ?? e).Message);
                            mapBroken = true; mapBusy = false; yield break;
                        }
                        bw.Write((byte)b); bw.Write((byte)Mathf.Clamp(Mathf.RoundToInt(h), 0, 255));
                    }
            }
            mapZones = n;
            string path = Path.Combine(outDir, "mapa.bin");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    File.WriteAllBytes(path + ".tmp", data);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(path + ".tmp", path);
                }
                catch (Exception e) { Logger.LogWarning("Majinheim Mundo: no pude guardar mapa.bin: " + e.Message); }
            });
            mapBusy = false;
        }

        // ------------------------------------------------------------ chat de la partida: preguntas a MajinBot (!majin)
        // El chat normal viaja como "Say", dirigido al jugador que habla, y gritar (/s) como "ChatMessage". El server los reparte sin
        // mostrarlos, y el "Say" ni siquiera llega a Chat en un server sin pantalla: por eso se mira en el punto por donde pasan todos,
        // sin tocarlos. Solo se apuntan los mensajes que empiezan por "!majin": el resto del chat no se guarda. El launcher los lee,
        // pregunta a la IA y contesta en el chat por RCON.

        bool chatHooked;
        static Plugin self;
        static FieldInfo rpcMethod, rpcParams;
        static int saidHash, shoutHash;

        void HookChat()
        {
            chatHooked = true;
            try
            {
                var target = typeof(ZRoutedRpc).GetMethod("HandleRoutedRPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var ps = target != null ? target.GetParameters() : null;
                if (ps != null && ps.Length == 1)
                {
                    rpcMethod = ps[0].ParameterType.GetField("m_methodHash");
                    rpcParams = ps[0].ParameterType.GetField("m_parameters");
                }
                if (rpcMethod == null || rpcParams == null) { Logger.LogWarning("Majinheim Mundo: no encuentro por dónde pasan los mensajes en esta versión de Valheim; !majin no funcionará."); return; }
                self = this; saidHash = Hash("Say"); shoutHash = Hash("ChatMessage");
                new Harmony("majinheim.mundo").Patch(target, new HarmonyMethod(typeof(Plugin).GetMethod("BeforeRpc", BindingFlags.Static | BindingFlags.NonPublic)));
                Logger.LogInfo("Majinheim Mundo: escuchando !majin en el chat de la partida.");
            }
            catch (Exception e) { Logger.LogWarning("Majinheim Mundo: no puedo escuchar el chat: " + e.Message); }
        }

        // Se mira y se deja como estaba: el mensaje sigue su camino igual, y un mensaje raro nunca debe estorbar al server.
        static void BeforeRpc(object __0)
        {
            try
            {
                int method = (int)rpcMethod.GetValue(__0);
                if (method != saidHash && method != shoutHash) return;
                var p = rpcParams.GetValue(__0) as ZPackage;
                if (p == null) return;
                int pos = p.GetPos();
                try
                {
                    if (method == shoutHash) p.ReadVector3();       // desde dónde se ha gritado
                    p.ReadInt();                                    // tipo de mensaje
                    string name = p.ReadString(); p.ReadString();   // quién habla: nombre e identificador
                    self.OnChatLine(name, p.ReadString());
                }
                finally { p.SetPos(pos); }
            }
            catch { }
        }

        void OnChatLine(string name, string text)
        {
            try
            {
                text = (text ?? "").Trim();
                if (!text.StartsWith("!majin", StringComparison.OrdinalIgnoreCase)) return;
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
            int nChests = 0, nDrawers = 0, nDrawersAll = 0, nTombs = 0, nSigns = 0, nPortals = 0, nBad = 0;
            unknownInventory = 0;
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
                        var list = ReadItems(z);
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
                            // El juego apunta la hora del mundo, que solo corre con el server encendido: de ahí sale una fecha aproximada.
                            long death = z.GetLong("timeOfDeath", 0L);
                            var ago = death > 0 && ZNet.instance != null ? ZNet.instance.GetTime() - new DateTime(death) : TimeSpan.Zero;
                            if (ago > TimeSpan.Zero) sb.Append(",\"muerte\":").Append(Q((DateTime.Now - ago).ToString("yyyy-MM-ddTHH:mm:ss", Inv)));
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
                        nDrawersAll++;
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
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
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
            if (unknownInventory != 0) Logger.LogWarning("Majinheim Mundo: hay cofres guardados en un formato que no conozco (versión " + unknownInventory + "): hay que actualizar el mod.");
            Logger.LogInfo(string.Format(Inv, "Majinheim Mundo: {0} cofres con algo, {1} cajones con algo (de {2}), {3} tumbas, {4} carteles y {5} portales ({6:0.0} s, {7} objetos del mundo).",
                nChests, nDrawers, nDrawersAll, nTombs, nSigns, nPortals, Time.realtimeSinceStartup - t0, zdos.Count));
            busy = false;
        }

        struct Item { public string Id; public int Stack, Quality; }

        // Lo que hay dentro de un cofre, tal como lo guarda Valheim (Inventory.Save), sin crear nada en el mundo. Es a mano porque
        // Inventory.Load crea cada objeto, y eso frenaría el server. Desde la versión 108 cada objeto va compacto y se identifica por
        // el hash de su prefab; los cofres que nadie ha abierto desde entonces siguen en el formato antiguo, con el nombre.
        const int NewestInventory = 110;
        static readonly int ItemsKey = Hash("items");
        int unknownInventory;

        List<Item> ReadItems(ZDO z)
        {
            var res = new List<Item>();
            byte[] data = z.GetByteArray(ItemsKey, null);               // desde Valheim 1.0 son bytes; antes, texto en base64
            string b64 = data == null ? z.GetString("items", "") : null;
            if (data == null && string.IsNullOrEmpty(b64)) return res;
            var p = data != null ? new ZPackage(data) : new ZPackage(b64);
            int ver = p.ReadInt();
            if (ver < 100 || ver > NewestInventory) { unknownInventory = ver; return res; }   // formato que no conozco: mejor no inventar
            if (ver >= 108) ReadCompact(p, ver, res); else ReadOld(p, ver, res);
            return res;
        }

        // Como ItemDrop.ItemData.Save: durabilidad, posición y nivel del mundo; un byte de banderas dice qué más viene.
        void ReadCompact(ZPackage p, int ver, List<Item> res)
        {
            int count = p.ReadUShort();
            for (int i = 0; i < count; i++)
            {
                p.ReadInt();                                        // durabilidad
                p.ReadByte(); p.ReadByte(); p.ReadByte();           // posición en el cofre y nivel del mundo
                int flags = p.ReadByte();
                var it = new Item { Quality = 1, Stack = 1 };
                if ((flags & 4) != 0) it.Quality = p.ReadUShort();
                if ((flags & 8) != 0) it.Stack = p.ReadUShort();
                if ((flags & 16) != 0) p.ReadInt();                 // variante
                if ((flags & 32) != 0) { p.ReadLong(); p.ReadString(); }   // quién lo fabricó
                int prefab = (flags & 64) != 0 ? p.ReadInt() : 0;
                int extra = (flags & 128) != 0 ? p.ReadNumItems() : 0;
                for (int k = 0; k < extra; k++) { p.ReadString(); p.ReadString(); }
                if (ver >= 109) p.ReadByte();                       // otras banderas
                if (prefab != 0 && prefabNames.TryGetValue(prefab, out it.Id) && it.Stack > 0) res.Add(it);
            }
        }

        static void ReadOld(ZPackage p, int ver, List<Item> res)
        {
            int count = p.ReadInt();
            if (count < 0 || count > 1000) return;
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
                if (ver == 107) p.ReadBool();   // otra bandera
                if (!string.IsNullOrEmpty(it.Id) && it.Stack > 0) res.Add(it);
            }
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
