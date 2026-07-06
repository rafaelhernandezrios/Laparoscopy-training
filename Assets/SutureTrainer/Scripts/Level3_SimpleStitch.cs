using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Nivel 3 — Punto simple: pasar la aguja por entrada→salida en cada par
    /// de dianas sobre la herida y tirar del hilo. Base también para el nivel 4.
    /// </summary>
    public class Level3_SimpleStitch : TrainingLevel
    {
        [Header("Configuración de sutura")]
        public int stitchPairs = 3;
        public bool cinchAfterEachStitch = true;
        public float pairSpacing = 0.075f;
        public float woundOffset = 0.045f; // distancia lateral de la diana a la herida
        public string customTitle;

        protected override string LevelTitle =>
            string.IsNullOrEmpty(customTitle) ? "Nivel 3 · Punto simple" : customTitle;

        protected SutureNeedle needle;
        protected VerletThread thread;
        protected TissuePatch tissue;
        protected PunctureTarget[] entries, exits;

        protected int currentPair;
        protected enum Phase { GoToEntry, InsideTissue, PullThrough, Done }
        protected Phase phase = Phase.GoToEntry;

        protected override void Setup()
        {
            tissue = SpawnTissue();
            var tray = SpawnTray(fieldRoot.position + new Vector3(0.20f, 0.03f, -0.12f));
            needle = SpawnNeedle(tray.transform.position + Vector3.up * 0.02f,
                Quaternion.Euler(90f, 0f, 0f));
            thread = SpawnThread(needle);
            tissue.trackedNeedle = needle;

            entries = new PunctureTarget[stitchPairs];
            exits = new PunctureTarget[stitchPairs];
            float startX = -pairSpacing * (stitchPairs - 1) * 0.5f;
            for (int i = 0; i < stitchPairs; i++)
            {
                Vector3 basePos = tissue.WoundCenter + tissue.WoundDir * (startX + i * pairSpacing);
                Vector3 side = Vector3.Cross(Vector3.up, tissue.WoundDir).normalized;
                entries[i] = SpawnPunctureTarget(basePos + side * woundOffset + Vector3.up * 0.014f, true, i);
                exits[i] = SpawnPunctureTarget(basePos - side * woundOffset + Vector3.up * 0.014f, false, i);
            }
            ActivatePair(0);
            SetObjective("Toma la aguja y clávala en la diana de ENTRADA iluminada (verde), atravesando hacia la salida.");
        }

        protected void ActivatePair(int i)
        {
            currentPair = i;
            phase = Phase.GoToEntry;
            entries[i].SetState(PunctureTarget.State.Active);
            tissue.punctureAllowed = false;
        }

        protected override void Tick()
        {
            if (needle == null) return;
            Vector3 tip = needle.TipPos;
            bool held = needle.Grasp.IsHeld;

            switch (phase)
            {
                case Phase.GoToEntry:
                    if (held && entries[currentPair].TipInside(tip) && needle.TipDir.y < 0.1f)
                    {
                        Metrics.AddPrecisionSample(entries[currentPair].Deviation(tip));
                        entries[currentPair].SetState(PunctureTarget.State.Done);
                        exits[currentPair].SetState(PunctureTarget.State.Active);
                        tissue.punctureAllowed = true;
                        phase = Phase.InsideTissue;
                        FlashInfo("Entrada correcta · gira la muñeca siguiendo la curva de la aguja");
                        SetObjective("Rota la aguja siguiendo su curvatura hasta salir por la diana de SALIDA.");
                        if (rightArm != null) rightArm.master.Haptic(0.5f, 0.08f);
                    }
                    break;

                case Phase.InsideTissue:
                    if (held && exits[currentPair].TipInside(tip))
                    {
                        Metrics.AddPrecisionSample(exits[currentPair].Deviation(tip));
                        exits[currentPair].SetState(PunctureTarget.State.Done);
                        thread.PinThroughStitch(exits[currentPair].transform.position,
                                                entries[currentPair].transform.position);
                        tissue.punctureAllowed = false;
                        phase = Phase.PullThrough;
                        SetObjective("Tira de la aguja para pasar el hilo, dejando una cola corta.");
                    }
                    break;

                case Phase.PullThrough:
                    // deja hilo suficiente para los puntos restantes
                    int tailTarget = 10 + (stitchPairs - 1 - currentPair) * 12;
                    if (thread.RemainingTailSegments <= tailTarget)
                        OnStitchThreadPulled();
                    break;
            }
        }

        protected virtual void OnStitchThreadPulled()
        {
            if (cinchAfterEachStitch)
            {
                Vector3 mid = (entries[currentPair].transform.position + exits[currentPair].transform.position) * 0.5f;
                thread.CinchTo(mid);
                FlashInfo($"Punto {currentPair + 1}/{stitchPairs} completado");
            }
            NextStitchOrFinish();
        }

        protected void NextStitchOrFinish()
        {
            phase = Phase.Done;
            if (currentPair + 1 >= stitchPairs)
            {
                OnAllStitchesDone();
            }
            else
            {
                ActivatePair(currentPair + 1);
                SetObjective($"Siguiente punto ({currentPair + 1}/{stitchPairs}): entra por la diana verde.");
            }
        }

        protected virtual void OnAllStitchesDone() => Finish();
    }
}
