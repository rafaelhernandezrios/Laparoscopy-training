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

        /// <summary>
        /// Diámetro visible de los marcadores de punción, en metros. Igual que
        /// el de los SuturePoint del órgano (MarkerRadius 0.003 x 2) para que
        /// todos los marcadores del campo se vean del mismo tamaño.
        /// </summary>
        protected const float MarkerDiameter = 0.006f;

        protected virtual string LevelTitle => "Nivel";

        void Start()
        {
            if (hud != null) hud.SetTitle(LevelTitle);
            Metrics.StartRun();
            Setup();
            // Los props del ejercicio ya existen: el lazo adaptativo puede fijar
            // radios de diana y umbrales de puntuación para este intento.
            AdaptiveDifficultyController.I?.ApplyBoundaryParameters();
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

        protected void FlashInfo(string msg)
        {
            if (hud != null) hud.Flash(msg, new Color(0.4f, 1f, 0.5f));
            AudioFX.Success();
        }

        protected void FlashError(string msg)
        {
            if (hud != null) hud.Flash(msg, new Color(1f, 0.35f, 0.3f));
            Metrics.AddError(msg);
            AudioFX.Error();
            leftArm?.master?.Haptic(0.9f, 0.2f);
            rightArm?.master?.Haptic(0.9f, 0.2f);
        }

        protected void Finish()
        {
            if (Finished) return;
            Finished = true;
            Metrics.StopRun();
            if (leftArm != null) leftArm.frozen = true;
            if (rightArm != null) rightArm.frozen = true;
            var score = Metrics.Evaluate(parTimeSec, parPathMeters, maxErrors);
            Metrics.Export(score);
            AdaptiveDifficultyController.I?.ExportTrace();
            AudioFX.Complete();
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
            // isActiveAndEnabled: soltar la aguja después de terminar el nivel
            // llegaría con el componente ya apagado, y StartCoroutine lanza
            // excepción en ese caso.
            grasp.onReleased += () =>
            {
                if (isActiveAndEnabled) StartCoroutine(ConfirmarCaida(grasp, go.transform));
            };
            return needle;
        }

        [Tooltip("Margen tras soltar la aguja antes de contarlo como caída.")]
        public float dropGraceSeconds = 1.5f;

        [Tooltip("Cuánto tiene que descender la aguja tras soltarla para " +
                 "considerarla caída, en metros.")]
        public float dropFallMeters = 0.05f;

        /// <summary>
        /// Distingue SOLTAR de que se te CAIGA la aguja.
        ///
        /// Antes cada onReleased contaba un error, y recolocar la aguja en el
        /// portaagujas —abrir, girar, volver a cerrar— es parte normal de
        /// suturar, no un fallo. El contador subía sin parar desde el principio:
        /// 11 errores en 24 s en la sesión del 28-08, ninguno por tensión (el
        /// hilo solo cruzó el umbral una vez en toda la grabación).
        ///
        /// Ahora se espera un margen y solo cuenta si de verdad se quedó sin
        /// agarrar Y cayó.
        /// </summary>
        System.Collections.IEnumerator ConfirmarCaida(Graspable grasp, Transform aguja)
        {
            if (grasp == null || aguja == null) yield break;

            float alturaAlSoltar = aguja.position.y;
            yield return new WaitForSeconds(dropGraceSeconds);

            if (Finished || !Metrics.Running) yield break;
            if (grasp == null || aguja == null) yield break;

            // Recogida a tiempo: era una recolocación, no una caída.
            if (grasp.IsHeld) yield break;

            if (alturaAlSoltar - aguja.position.y >= dropFallMeters)
                Metrics.AddNeedleDrop();
        }

        /// <summary>
        /// Fábrica de hilo intercambiable: asignar aquí una implementación
        /// alternativa (p. ej. Obi Rope) antes de que carguen los niveles.
        /// </summary>
        public static System.Func<GameObject, ThreadBase> ThreadFactory;

        protected ThreadBase SpawnThread(SutureNeedle needle)
        {
            var go = new GameObject("SutureThread");
            go.transform.SetParent(fieldRoot, false);
            ThreadBase th = ThreadFactory != null ? ThreadFactory(go) : go.AddComponent<VerletThread>();
            th.anchor = needle.Tail;
            th.onTensionEvent += () =>
            {
                if (Finished) return;
                Metrics.AddTensionEvent();
                AudioFX.Tension();
                leftArm?.master?.Haptic(0.7f, 0.15f);
                rightArm?.master?.Haptic(0.7f, 0.15f);
            };
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
                if (!Finished && !PuncionEsperada(p)) FlashError("Punción fuera de la diana");
            };
            return t;
        }

        /// <summary>
        /// ¿La punta está donde SE ESPERA que esté, aunque el parche lógico la
        /// considere una punción indebida?
        ///
        /// TissuePatch marca error en cuanto la punta baja de su plano sin que
        /// el nivel haya abierto punctureAllowed, y ese permiso solo está
        /// abierto entre la entrada y la salida del punto. Pero con el órgano
        /// real el plano lógico se alinea con la herida, que es una zanja: para
        /// apuntar a la diana de entrada hay que estar ahí abajo. Resultado, un
        /// error cada 1.5 s -el limitador de TissuePatch- solo por colocarse.
        /// En la sesión del 28-08 eran 5 errores en 8 segundos, todos falsos.
        ///
        /// Cada nivel decide qué zona es legítima; por defecto, ninguna.
        /// </summary>
        protected virtual bool PuncionEsperada(Vector3 tip) => false;

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
            // El tamaño VISIBLE se desacopla de la tolerancia.
            //
            // Antes se escalaba con t.radius (0.028 m de tolerancia de captura),
            // lo que daba discos de 45 mm: siete veces los marcadores del
            // órgano y visualmente enormes sobre la incisión. La tolerancia no
            // se toca —cambiarla alteraría la dificultad calibrada del nivel—,
            // solo se dibuja del tamaño de los marcadores del rig (6 mm).
            t.visualDiameter = MarkerDiameter;
            t.ApplyVisual();

            if (MaterialSet.I != null)
                t.Init(MaterialSet.I.markerIdle, isEntry ? MaterialSet.I.markerEntry : MaterialSet.I.markerExit, MaterialSet.I.markerDone);
            return t;
        }
    }
}
