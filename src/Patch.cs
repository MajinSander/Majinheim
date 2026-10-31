// Arreglo de kg_ItemDrawers (KGvalheim, QuickFix 1.2.1 de Firehunter1980) para Valheim 1.0.
// Lo aplica cada launcher en su PC sobre el mod original bajado de Thunderstore: así no repartimos mods modificados.
// No cambia nombres de prefabs ni datos: los cajones ya construidos siguen siendo los mismos.
// (Es la misma reparación que src\patches\Patch-ItemDrawers.ps1)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Majinheim
{
    public static class CecilLoader
    {
        static bool hooked;
        // Mono.Cecil viene con BepInEx: lo cargamos de la carpeta core del perfil.
        public static void Hook(string profile)
        {
            if (hooked) return;
            hooked = true;
            string core = Path.Combine(profile, @"BepInEx\core");
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string name = new AssemblyName(e.Name).Name;
                if (!name.StartsWith("Mono.Cecil")) return null;
                string p = Path.Combine(core, name + ".dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };
        }
    }

    public static class DrawerPatch
    {
        public const string Name = "itemdrawers-1.0";

        public static List<string> Apply(string dll, string managed)
        {
            var log = new List<string>();
            var res = new DefaultAssemblyResolver();
            res.AddSearchDirectory(managed);
            res.AddSearchDirectory(Path.GetDirectoryName(dll));
            var rp = new ReaderParameters { AssemblyResolver = res, InMemory = true };
            var asm = AssemblyDefinition.ReadAssembly(dll, rp);
            var mod = asm.MainModule;
            var game = res.Resolve(AssemblyNameReference.Parse("assembly_valheim")).MainModule;
            Func<string, string, int, MethodDefinition> gameMethod = (t, n, np) => game.GetType(t).Methods.First(x => x.Name == n && x.Parameters.Count == np);

            // 1) Hoverable.GetHoverOffset (nuevo en 1.0)
            var dc = mod.GetType("kg_ItemDrawers.DrawerComponent");
            if (!dc.Methods.Any(x => x.Name == "GetHoverOffset"))
            {
                var m = new MethodDefinition("GetHoverOffset", MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot, mod.TypeSystem.Single);
                var il = m.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldc_R4, 0f));
                il.Append(il.Create(OpCodes.Ret));
                dc.Methods.Add(m);
                log.Add("1) GetHoverOffset");
            }

            var newAvail = mod.ImportReference(game.GetType("PieceTable").Fields.First(f => f.Name == "m_availablePiecesByCategory"));
            var changed2 = mod.ImportReference(gameMethod("Inventory", "Changed", 2));
            var show6 = mod.ImportReference(gameMethod("MessageHud", "ShowMessage", 6));

            foreach (var t in mod.GetTypes())
                foreach (var m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    var il = m.Body.GetILProcessor();
                    foreach (var i in m.Body.Instructions.ToList())
                    {
                        var fr = i.Operand as FieldReference;
                        var mr = i.Operand as MethodReference;
                        // 2) PieceTable.m_availablePieces -> m_availablePiecesByCategory
                        if (fr != null && fr.DeclaringType.Name == "PieceTable" && fr.Name == "m_availablePieces")
                        { i.Operand = newAvail; log.Add("2) " + t.Name + "." + m.Name); }
                        // 3) Inventory.Changed() -> Changed(false, false)
                        else if (mr != null && mr.DeclaringType.Name == "Inventory" && mr.Name == "Changed" && mr.Parameters.Count == 0)
                        { il.InsertBefore(i, il.Create(OpCodes.Ldc_I4_0)); il.InsertBefore(i, il.Create(OpCodes.Ldc_I4_0)); i.Operand = changed2; log.Add("3) " + t.Name + "." + m.Name); }
                        // 4) MessageHud.ShowMessage(5 args) -> 6 args (log = true, el valor por defecto del juego)
                        else if (mr != null && mr.DeclaringType.Name == "MessageHud" && mr.Name == "ShowMessage" && mr.Parameters.Count == 5)
                        { il.InsertBefore(i, il.Create(OpCodes.Ldc_I4_1)); i.Operand = show6; log.Add("4) " + t.Name + "." + m.Name); }
                        // 7) campos que ahora son constantes (p.ej. ZRoutedRpc.Everybody): cargar el valor directamente
                        else if (fr != null && i.OpCode.Code == Code.Ldsfld && fr.DeclaringType.Scope.Name.StartsWith("assembly_"))
                        {
                            var fd = fr.Resolve();
                            if (fd == null || !fd.IsLiteral) continue;
                            object c = fd.Constant;
                            switch (fd.FieldType.FullName)
                            {
                                case "System.Int64": i.OpCode = OpCodes.Ldc_I8; i.Operand = Convert.ToInt64(c); break;
                                case "System.Int32": i.OpCode = OpCodes.Ldc_I4; i.Operand = Convert.ToInt32(c); break;
                                case "System.Single": i.OpCode = OpCodes.Ldc_R4; i.Operand = Convert.ToSingle(c); break;
                                case "System.String": i.OpCode = OpCodes.Ldstr; i.Operand = (string)c; break;
                                default: throw new Exception("Constante no soportada: " + fd.FullName);
                            }
                            log.Add("7) " + t.Name + "." + m.Name + ": " + fd.Name);
                        }
                    }
                }

            // 5) Quitar el conversor de cajones de Makail (parchea ZDOMan.Load, que en 1.0 ya no es como antes)
            foreach (var t in mod.GetTypes().Where(x => x.Name == "ZDOMan_Load_Patch"))
            {
                foreach (var ca in t.CustomAttributes.Where(a => a.AttributeType.Name.StartsWith("Harmony")).ToList()) t.CustomAttributes.Remove(ca);
                foreach (var m in t.Methods)
                    foreach (var ca in m.CustomAttributes.Where(a => a.AttributeType.Name.StartsWith("Harmony")).ToList()) m.CustomAttributes.Remove(ca);
                log.Add("5) " + t.Name);
            }

            // 6) AudioMan_Awake_Patch: el grupo de sonido "SFX" puede no existir; si no está, no hacer nada
            foreach (var t in mod.GetTypes().Where(x => x.Name == "AudioMan_Awake_Patch"))
            {
                var m = t.Methods.First(x => x.Name == "Postfix");
                var il = m.Body.GetILProcessor();
                var find = m.Body.Instructions.First(x => x.Operand is MethodReference && ((MethodReference)x.Operand).Name == "FindMatchingGroups");
                var tmp = new VariableDefinition(((MethodReference)find.Operand).ReturnType);
                m.Body.Variables.Add(tmp);
                var after = find.Next;
                var cont = il.Create(OpCodes.Ldloc, tmp);
                il.InsertBefore(after, il.Create(OpCodes.Stloc, tmp));
                il.InsertBefore(after, il.Create(OpCodes.Ldloc, tmp));
                il.InsertBefore(after, il.Create(OpCodes.Ldlen));
                il.InsertBefore(after, il.Create(OpCodes.Brtrue, cont));
                il.InsertBefore(after, il.Create(OpCodes.Ret));
                il.InsertBefore(after, cont);
                log.Add("6) " + t.Name);
            }

            asm.Write(dll);
            return log;
        }
    }
}
