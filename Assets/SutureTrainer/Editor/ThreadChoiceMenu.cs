using UnityEditor;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Conmuta la implementación de hilo que usan los niveles, sin recompilar.
    /// Ver <see cref="ThreadBootstrap"/> para por qué existe el interruptor.
    /// </summary>
    public static class ThreadChoiceMenu
    {
        const string MenuPath = "SutureTrainer/Hilo: usar AdvancedRope";

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            bool nuevo = !ThreadBootstrap.UseAdvancedRope;
            PlayerPrefs.SetInt(ThreadBootstrap.PrefKey, nuevo ? 1 : 0);
            PlayerPrefs.Save();

            Debug.Log(nuevo
                ? "[Hilo] AdvancedRopeThread: un solo hilo, el del asset con físicas."
                : "[Hilo] VerletThread: el hilo propio del proyecto.");
        }

        [MenuItem(MenuPath, validate = true)]
        static bool Validate()
        {
            Menu.SetChecked(MenuPath, ThreadBootstrap.UseAdvancedRope);
            return true;
        }
    }
}
