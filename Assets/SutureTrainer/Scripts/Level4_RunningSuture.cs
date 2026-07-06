using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Nivel 4 — Sutura continua: 5 pases con el mismo hilo a lo largo de la
    /// herida, manteniendo tensión uniforme (sin cinchar entre puntos).
    /// </summary>
    public class Level4_RunningSuture : TrainingLevel
    {
        protected override string LevelTitle => "Nivel 4 · Sutura continua";

        Level3_SimpleStitch core;

        protected override void Setup()
        {
            // reutiliza la lógica del nivel 3 con configuración de sutura continua
            core = gameObject.AddComponent<Level3_SimpleStitch>();
            core.leftArm = leftArm; core.rightArm = rightArm;
            core.fieldRoot = fieldRoot; core.hud = hud; core.results = results;
            core.customTitle = LevelTitle;
            core.stitchPairs = 5;
            core.cinchAfterEachStitch = false;
            core.pairSpacing = 0.06f;
            core.parTimeSec = parTimeSec;
            core.parPathMeters = parPathMeters;
            core.maxErrors = maxErrors;
            // este componente delega todo en core
            enabled = false;
        }

        protected override void Tick() { }
    }
}
