using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Nivel 1 — Manejo de aguja: tomar la aguja de la bandeja con el
    /// porta-agujas derecho y alinearla con la pose fantasma. 3 repeticiones.
    /// </summary>
    public class Level1_NeedleHandling : TrainingLevel
    {
        public int repetitions = 3;
        public float posTolerance = 0.03f;
        public float angTolerance = 14f;
        public float holdTime = 0.8f;

        protected override string LevelTitle => "Nivel 1 · Manejo de aguja";

        SutureNeedle needle;
        Transform ghost;
        int done;
        float holdTimer;

        readonly Vector3[] ghostLocalPos =
        {
            new Vector3(-0.05f, 0.14f, 0.02f),
            new Vector3(0.08f, 0.17f, -0.03f),
            new Vector3(0.00f, 0.12f, 0.06f)
        };
        readonly Vector3[] ghostEuler =
        {
            new Vector3(0f, 30f, 90f),
            new Vector3(35f, -20f, 60f),
            new Vector3(-25f, 90f, 120f)
        };

        protected override void Setup()
        {
            var tray = SpawnTray(fieldRoot.position + new Vector3(0.16f, 0.03f, -0.06f));
            needle = SpawnNeedle(tray.transform.position + Vector3.up * 0.02f,
                Quaternion.Euler(90f, 0f, 0f));

            ghost = BuildGhost();
            PlaceGhost(0);
            SetObjective("Toma la aguja de la bandeja con el porta-agujas DERECHO (gatillo para cerrar) y alinéala con la aguja fantasma.");
        }

        Transform BuildGhost()
        {
            var go = new GameObject("GhostNeedle");
            go.transform.SetParent(fieldRoot, false);
            var mesh = ProcMesh.ArcTube(0.055f, 180f, 0.0035f, out _, out _, out _, out _);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (MaterialSet.I != null) mr.sharedMaterial = MaterialSet.I.ghost;
            return go.transform;
        }

        void PlaceGhost(int i)
        {
            ghost.localPosition = ghostLocalPos[i % ghostLocalPos.Length];
            ghost.localRotation = Quaternion.Euler(ghostEuler[i % ghostEuler.Length]);
        }

        protected override void Tick()
        {
            if (needle == null || ghost == null) return;
            if (!needle.Grasp.IsHeld) { holdTimer = 0f; return; }

            float dPos = Vector3.Distance(needle.transform.position, ghost.position);
            float dAng = Quaternion.Angle(needle.transform.rotation, ghost.rotation);
            // la aguja es simétrica al girar 180° sobre su plano
            dAng = Mathf.Min(dAng, Quaternion.Angle(needle.transform.rotation,
                ghost.rotation * Quaternion.Euler(0f, 180f, 0f)));

            bool aligned = dPos < posTolerance && dAng < angTolerance;
            if (aligned)
            {
                holdTimer += Time.deltaTime;
                if (holdTimer >= holdTime)
                {
                    Metrics.AddPrecisionSample(dPos);
                    done++;
                    holdTimer = 0f;
                    if (done >= repetitions)
                    {
                        FlashInfo("¡Ejercicio completado!");
                        ghost.gameObject.SetActive(false);
                        Finish();
                    }
                    else
                    {
                        FlashInfo($"Pose {done}/{repetitions} conseguida");
                        PlaceGhost(done);
                        SetObjective($"Alinea la aguja con la nueva pose fantasma ({done + 1}/{repetitions}).");
                    }
                }
            }
            else holdTimer = 0f;
        }
    }
}
