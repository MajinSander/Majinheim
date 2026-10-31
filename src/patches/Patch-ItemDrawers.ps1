# Repara kg_ItemDrawers (KGvalheim, version QuickFix 1.2.1 de Firehunter1980) para Valheim 1.0.
# No cambia nombres de prefabs ni datos: los cajones ya construidos siguen siendo los mismos.
param(
    [string]$In = "C:\Majinheim\src\patches\original\kg_ItemDrawers.dll",
    [string]$Out = "C:\Majinheim\src\patches\out\kg_ItemDrawers.dll",
    [string]$Managed = "C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed"
)
$ErrorActionPreference = 'Stop'
Add-Type -Path "C:\Majinheim\profiles\server\BepInEx\core\Mono.Cecil.dll"
$res = New-Object Mono.Cecil.DefaultAssemblyResolver
$res.AddSearchDirectory($Managed)
$res.AddSearchDirectory("C:\Majinheim\profiles\server\BepInEx\core")
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $res
$rp.InMemory = $true
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($In, $rp)
$mod = $asm.MainModule
$game = $res.Resolve([Mono.Cecil.AssemblyNameReference]::Parse("assembly_valheim")).MainModule
$OC = [Mono.Cecil.Cil.OpCodes]
$log = New-Object System.Collections.Generic.List[string]

function GameMethod($type, $name, $nparams) { $game.GetType($type).Methods | ? { $_.Name -eq $name -and $_.Parameters.Count -eq $nparams } | Select -First 1 }

# 1) Hoverable.GetHoverOffset (nuevo en 1.0)
$dc = $mod.GetType("kg_ItemDrawers.DrawerComponent")
if (-not ($dc.Methods | ? Name -eq 'GetHoverOffset')) {
    $attrs = [Mono.Cecil.MethodAttributes]'Public, Final, Virtual, HideBySig, NewSlot'
    $m = New-Object Mono.Cecil.MethodDefinition 'GetHoverOffset', $attrs, $mod.TypeSystem.Single
    $il = $m.Body.GetILProcessor()
    $il.Append($il.Create($OC::Ldc_R4, [single]0))
    $il.Append($il.Create($OC::Ret))
    $dc.Methods.Add($m)
    $log.Add("1) Anadido DrawerComponent.GetHoverOffset()")
}

$newAvail = $mod.ImportReference(($game.GetType('PieceTable').Fields | ? Name -eq 'm_availablePiecesByCategory'))
$changed2 = $mod.ImportReference((GameMethod 'Inventory' 'Changed' 2))
$show6 = $mod.ImportReference((GameMethod 'MessageHud' 'ShowMessage' 6))

foreach ($t in $mod.GetTypes()) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        $il = $m.Body.GetILProcessor()
        $ins = @($m.Body.Instructions)
        foreach ($i in $ins) {
            $op = $i.Operand
            # 2) PieceTable.m_availablePieces -> m_availablePiecesByCategory
            if ($op -is [Mono.Cecil.FieldReference] -and $op.DeclaringType.Name -eq 'PieceTable' -and $op.Name -eq 'm_availablePieces') {
                $i.Operand = $newAvail; $log.Add("2) $($t.Name).$($m.Name): m_availablePieces -> m_availablePiecesByCategory")
            }
            # 3) Inventory.Changed() -> Changed(false, false)
            elseif ($op -is [Mono.Cecil.MethodReference] -and $op.DeclaringType.Name -eq 'Inventory' -and $op.Name -eq 'Changed' -and $op.Parameters.Count -eq 0) {
                $il.InsertBefore($i, $il.Create($OC::Ldc_I4_0)); $il.InsertBefore($i, $il.Create($OC::Ldc_I4_0)); $i.Operand = $changed2
                $log.Add("3) $($t.Name).$($m.Name): Inventory.Changed() -> Changed(false,false)")
            }
            # 4) MessageHud.ShowMessage(5 args) -> 6 args (log = true, el valor por defecto del juego)
            elseif ($op -is [Mono.Cecil.MethodReference] -and $op.DeclaringType.Name -eq 'MessageHud' -and $op.Name -eq 'ShowMessage' -and $op.Parameters.Count -eq 5) {
                $il.InsertBefore($i, $il.Create($OC::Ldc_I4_1)); $i.Operand = $show6
                $log.Add("4) $($t.Name).$($m.Name): ShowMessage con 6 parametros")
            }
            # 7) campos que ahora son constantes (p.ej. ZRoutedRpc.Everybody): cargar el valor directamente
            elseif ($op -is [Mono.Cecil.FieldReference] -and $i.OpCode.Code -eq 'Ldsfld' -and $op.DeclaringType.Scope.Name -match 'assembly_') {
                $fd = $op.Resolve()
                if ($fd -ne $null -and $fd.IsLiteral) {
                    $c = $fd.Constant
                    switch ($fd.FieldType.FullName) {
                        'System.Int64' { $i.OpCode = $OC::Ldc_I8; $i.Operand = [long]$c }
                        'System.Int32' { $i.OpCode = $OC::Ldc_I4; $i.Operand = [int]$c }
                        'System.Single' { $i.OpCode = $OC::Ldc_R4; $i.Operand = [single]$c }
                        'System.String' { $i.OpCode = $OC::Ldstr; $i.Operand = [string]$c }
                        default { throw "Constante no soportada: $($fd.FullName)" }
                    }
                    $log.Add("7) $($t.Name).$($m.Name): $($fd.DeclaringType.Name).$($fd.Name) ahora es constante ($c)")
                }
            }
        }
    }
}

# 5) Quitar el conversor de cajones de Makail (parchea ZDOMan.Load, que en 1.0 ya no es como antes)
foreach ($t in $mod.GetTypes()) {
    if ($t.Name -ne 'ZDOMan_Load_Patch') { continue }
    foreach ($ca in @($t.CustomAttributes)) { if ($ca.AttributeType.Name -like 'Harmony*') { [void]$t.CustomAttributes.Remove($ca) } }
    foreach ($m in $t.Methods) { foreach ($ca in @($m.CustomAttributes)) { if ($ca.AttributeType.Name -like 'Harmony*') { [void]$m.CustomAttributes.Remove($ca) } } }
    $log.Add("5) Desactivado $($t.FullName) (conversor de Makail)")
}

# 6) AudioMan_Awake_Patch: el grupo de sonido "SFX" puede no existir; si no esta, no hacer nada
foreach ($t in $mod.GetTypes()) {
    if ($t.Name -ne 'AudioMan_Awake_Patch') { continue }
    $m = $t.Methods | ? Name -eq 'Postfix'
    $il = $m.Body.GetILProcessor()
    $find = $m.Body.Instructions | ? { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'FindMatchingGroups' } | Select -First 1
    $tmp = New-Object Mono.Cecil.Cil.VariableDefinition ($find.Operand.ReturnType)
    $m.Body.Variables.Add($tmp)
    $after = $find.Next          # ldc.i4.0 (indice 0)
    $il.InsertBefore($after, $il.Create($OC::Stloc, $tmp))
    $il.InsertBefore($after, $il.Create($OC::Ldloc, $tmp))
    $il.InsertBefore($after, $il.Create($OC::Ldlen))
    $retIns = $il.Create($OC::Ret)
    $cont = $il.Create($OC::Ldloc, $tmp)
    $il.InsertBefore($after, $il.Create($OC::Brtrue, $cont))
    $il.InsertBefore($after, $retIns)
    $il.InsertBefore($after, $cont)
    $log.Add("6) AudioMan_Awake_Patch.Postfix: protegido si no existe el grupo 'SFX'")
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$asm.Write($Out)
$log | % { "  $_" }
"Escrito: $Out"
