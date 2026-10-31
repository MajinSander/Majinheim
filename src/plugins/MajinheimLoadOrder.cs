// Plugin propio de Majinheim. Su GUID empieza por "0" para que BepInEx lo procese el primero,
// y como depende de Jotunn, Jotunn se carga antes que todo lo demas.
// Arregla Cybrp ItemDrawers, que no declara Jotunn como dependencia y AzuCraftyBoxes lo adelanta.
using BepInEx;

namespace Majinheim
{
    [BepInPlugin("0majinheim.loadorder", "Majinheim LoadOrderFix", "1.0.0")]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.SoftDependency)]
    public class LoadOrderFix : BaseUnityPlugin
    {
        void Awake()
        {
            Logger.LogInfo("Orden de carga ajustado: Jotunn va antes que el resto de mods.");
        }
    }
}
