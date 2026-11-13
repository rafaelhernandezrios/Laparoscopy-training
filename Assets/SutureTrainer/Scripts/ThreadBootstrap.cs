using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Decide qué implementación de hilo usan los niveles.
    ///
    /// Por defecto <see cref="AdvancedRopeThread"/>, para que haya UN solo hilo
    /// en todo el proyecto: el del asset con físicas. Antes convivían dos
    /// —VerletThread en los niveles y el rope del asset solo en ST_06— y era
    /// justo al revés de lo deseable, porque la escena con mecánica de sutura
    /// usaba el casero.
    ///
    /// El interruptor existe porque este cambio toca el camino crítico de los
    /// cinco niveles a la vez. Si el hilo nuevo se porta mal, se vuelve al de
    /// siempre desde **SutureTrainer → Hilo: usar AdvancedRope** sin recompilar
    /// ni revertir nada.
    /// </summary>
    public static class ThreadBootstrap
    {
        public const string PrefKey = "SutureTrainer.UseAdvancedRope";

        public static bool UseAdvancedRope =>
            PlayerPrefs.GetInt(PrefKey, 1) != 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (!UseAdvancedRope)
            {
                // null = TrainingLevel cae a VerletThread, su comportamiento
                // histórico.
                TrainingLevel.ThreadFactory = null;
                Debug.Log("[Hilo] VerletThread (el propio del proyecto).");
                return;
            }

            TrainingLevel.ThreadFactory = go => go.AddComponent<AdvancedRopeThread>();
            Debug.Log("[Hilo] AdvancedRopeThread (asset con físicas).");
        }
    }
}
