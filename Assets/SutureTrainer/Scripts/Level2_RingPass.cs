using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Nivel 2 — Anillos: pasar la punta de la aguja por 4 anillos en orden.
    /// Mide la desviación respecto al centro de cada anillo.
    /// </summary>
    public class Level2_RingPass : TrainingLevel
    {
        protected override string LevelTitle => "Nivel 2 · Precisión en anillos";

        SutureNeedle needle;
        RingTarget[] rings;
        int current;

        protected override void Setup()
        {
            var tray = SpawnTray(fieldRoot.position + new Vector3(0.18f, 0.03f, -0.08f));
            needle = SpawnNeedle(tray.transform.position + Vector3.up * 0.02f,
                Quaternion.Euler(90f, 0f, 0f));

            Vector3[] localPos =
            {
                new Vector3(-0.12f, 0.10f, 0.00f),
                new Vector3(-0.04f, 0.16f, 0.05f),
                new Vector3(0.05f, 0.12f, -0.04f),
                new Vector3(0.13f, 0.18f, 0.02f)
            };
            Vector3[] euler =
            {
                new Vector3(0f, 90f, 0f),
                new Vector3(20f, 60f, 0f),
                new Vector3(-15f, 110f, 0f),
                new Vector3(10f, 80f, 0f)
            };

            rings = new RingTarget[localPos.Length];
            for (int i = 0; i < localPos.Length; i++)
            {
                var go = new GameObject($"Ring_{i}");
                go.transform.SetParent(fieldRoot, false);
                go.transform.localPosition = localPos[i];
                go.transform.localRotation = Quaternion.Euler(euler[i]);
                var ring = go.AddComponent<RingTarget>();
                if (MaterialSet.I != null)
                    ring.Build(MaterialSet.I.ring, MaterialSet.I.ringActive, MaterialSet.I.ringDone);
                int idx = i;
                ring.onPassed += (r, dev) =>
                {
                    Metrics.AddPrecisionSample(dev);
                    FlashInfo($"Anillo {idx + 1}/4 · desviación {(dev * 1000f):0} mm");
                    Advance();
                };
                rings[i] = ring;
            }
            rings[0].IsActive = true;
            SetObjective("Toma la aguja y pasa la PUNTA por el anillo iluminado (1/4).");
        }

        void Advance()
        {
            current++;
            if (current >= rings.Length)
            {
                Finish();
                return;
            }
            rings[current].IsActive = true;
            SetObjective($"Pasa la punta por el siguiente anillo ({current + 1}/4).");
        }

        protected override void Tick()
        {
            if (needle == null || !needle.Grasp.IsHeld) return;
            foreach (var r in rings) r.CheckTip(needle.TipPos);
        }
    }
}
