using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Desconecta los aparatos hápticos al terminar el juego, una sola vez.
    ///
    /// disconnectAllDevices() es GLOBAL, no por aparato, así que no puede
    /// colgar del OnDestroy de cada mano: con dos robots, la primera mano en
    /// destruirse desconectaría también la del compañero. Cada MasterInput solo
    /// silencia el suyo; desconectar de verdad es cosa de aquí.
    ///
    /// Se instala solo, sin tocar las escenas.
    /// </summary>
    public static class TouchShutdown
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Instalar()
        {
            Application.quitting -= Cerrar;
            Application.quitting += Cerrar;

#if UNITY_EDITOR
            // En el editor no hay quitting al salir de Play: el dominio sigue
            // vivo y los aparatos quedarían conectados hasta reiniciar Unity.
            UnityEditor.EditorApplication.playModeStateChanged -= AlCambiarPlay;
            UnityEditor.EditorApplication.playModeStateChanged += AlCambiarPlay;
#endif
        }

#if UNITY_EDITOR
        static void AlCambiarPlay(UnityEditor.PlayModeStateChange estado)
        {
            if (estado == UnityEditor.PlayModeStateChange.ExitingPlayMode) Cerrar();
        }
#endif

        static void Cerrar() => TouchDevice.ShutdownAll();
    }
}
