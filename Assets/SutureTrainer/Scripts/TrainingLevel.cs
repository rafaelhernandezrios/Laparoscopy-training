using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Base de todos los niveles de entrenamiento. Gestiona objetivos,
    /// inicio/fin, puntuación y utilidades para crear los props del ejercicio.
    /// </summary>
    public abstract class TrainingLevel : MonoBehaviour
    {
        [Header("Referencias (SceneBuilder)")]
        public RoboticArm leftArm;
        public RoboticArm rightArm;
        public Transform fieldRoot;   // centro del campo quirúrgico
        public HUD hud;
        public ResultsPanel results;

        [Header("Puntuación (umbrales para 3 estrellas)")]
        public float parTimeSec = 120f;
        public float parPathMeters = 12f;
        public int maxErrors = 1;

        public bool Finished { get; private set; }

        protected MetricsRecorder Metrics => MetricsRecorder.I;

        protected virtual string LevelTitle => "Nivel";

        void Start()
        {
            if (hud != null) hud.SetTitle(LevelTitle);
            Metrics.StartRun();
            Setup();
        }

        void Update()
        {
            if (!Finished) Tick();
        }

        /// <summary>Crear props y establecer el primer objetivo.</summary>
        protected abstract void Setup();
        /// <summary>Lógica por frame del nivel.</summary>
        protected abstract void Tick();

        protected void SetObjective(string text)
        {
            if (hud != null) hud.SetObjective(text);
        }

        protected void FlashInfo(string msg) { if (hud != null) hud.Flash(msg, new Color(0.4f, 1f, 0.5f)); }
        protected void FlashError(string msg)
        {
            if (hud != null) hud.Flash(msg, new Color(1f, 0.35f, 0.3f));
            Metrics.AddError(msg);
        }

        protected void Finish()
        {
            if (Finished) return;
            Finished = true;
            Metrics.StopRun();
            if (leftArm != null) leftArm.frozen = true;
            if (rightArm != null) rightArm.frozen = true;
            var score = Metrics.Evaluate(parTimeSec, parPathMeters, maxErrors);
            if (results != null) results.Show(score);
            var laser = FindFirstObjectByType<ControllerLaser>();
            if (laser != null) laser.SetActive(true);
        }

        // ---------- utilidades de creación ----------

        protected SutureNeedle SpawnNeedle(Vector3 worldPos, Quaternion rot)
        {
            var go = new GameObject("SutureNeedle");
            go.transform.SetParent(fieldRoot, false);
            go.transform.SetPositionAndRotation(worldPos, rot);
            var needle = go.AddComponent<SutureNeedle>();
            var grasp = go.GetComponent<Graspable>();
            grasp.onReleased += () =>
            {
                if (!Finished && Metrics.Running) Metrics.AddNeedleDrop();
            };
            return needle;
        }

        protected VerletThread SpawnThread(SutureNeedle needle)
        {
            var go = new GameObject("SutureThread");
            go.transform.SetParent(fieldRoot, false);
            var th = go.AddComponent<VerletThread>();
            th.anchor = needle.Tail;
            th.onTensionEvent += () => { if (!Finished) Metrics.AddTensionEvent(); };
            return th;
        }

        protected GameObject SpawnTray(Vector3 worldPos)
        {
            var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tray.name = "Tray";
            tray.transform.SetParent(fieldRoot, false);
            tray.transform.position = worldPos;
            tray.transform.localScale = new Vector3(0.16f, 0.012f, 0.11f);
            if (MaterialSet.I != null)
                tray.GetComponent<MeshRenderer>().sharedMaterial = MaterialSet.I.darkMetal;
            return tray;
        }

        protected TissuePatch SpawnTissue()
        {
            var go = new GameObject("TissuePatch");
            go.transform.SetParent(fieldRoot, false);
            go.transform.localPosition = Vector3.zero;
            var t = go.AddComponent<TissuePatch>();
            t.onBadPuncture += p =>
            {
                if (!Finished) FlashError("Punción fuera de la diana");
            };
            return t;
        }

        protected PunctureTarget SpawnPunctureTarget(Vector3 worldPos, bool isEntry, int pairIndex)
        {
            var root = new GameObject(isEntry ? $"Entry_{pairIndex}" : $"Exit_{pairIndex}");
            root.transform.SetParent(fieldRoot, false);
            root.transform.position = worldPos;
            var t = root.AddComponent<PunctureTarget>();
            t.isEntry = isEntry; t.pairIndex = pairIndex;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            disc.name = "Marker";
            Object.Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localScale = new Vector3(t.radius * 1.6f, 0.004f, t.radius * 1.6f);

            if (MaterialSet.I != null)
                t.Init(MaterialSet.I.markerIdle, isEntry ? MaterialSet.I.markerEntry : MaterialSet.I.markerExit, MaterialSet.I.markerDone);
            return t;
        }
    }
}
