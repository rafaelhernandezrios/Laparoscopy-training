using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Nivel 4 — Sutura continua: 5 pases con el mismo hilo a lo largo de la
    /// herida, manteniendo tensión uniforme (sin cinchar entre puntos).
    /// Reutiliza Level3 por herencia (mismo patrón que Level5).
    /// </summary>
    public class Level4_RunningSuture : Level3_SimpleStitch
    {
        protected override string LevelTitle => "Nivel 4 · Sutura continua";

        void Awake()
        {
            stitchPairs = 5;
            cinchAfterEachStitch = false;
            pairSpacing = 0.06f;
        }
    }
}
