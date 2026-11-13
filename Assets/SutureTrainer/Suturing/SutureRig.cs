using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Sustituye a ProxyMovement de SuturingSimulator.
    ///
    /// INVARIANTE DE DISEÑO: allí el proxy de la aguja se movía leyendo WASD y
    /// el stylus del Touch DIRECTAMENTE (`hPlugin.Buttons[0]`, `Buttons[1]`),
    /// lo que ataba el sistema de sutura a un único dispositivo.
    ///
    /// Aquí la fuente es <see cref="RoboticArm"/>, que a su vez lee
    /// <see cref="MasterInput"/>. Como MasterInput ya conmuta entre mando de
    /// Quest y 3D Systems Touch (backend + tecla F1), el subsistema de sutura
    /// hereda esa conmutación sin saber nada de hápticos. Nunca debe volver a
    /// referenciar HapticPlugin: eso rompería el cambio de controles.
    ///
    /// La detección de puntos NO usa callbacks de trigger sino
    /// Physics.OverlapSphere (ver <see cref="TryCapture"/>), así que no depende
    /// de la configuración de Rigidbody ni de que el cuerpo esté despierto.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SutureRig : MonoBehaviour
    {
        [Header("Origen del control (nunca HapticPlugin)")]
        public RoboticArm arm;

        [Header("Subsistema portado de SuturingSimulator")]
        public SutureManager sutureManager;
        public BoyRopePinHole ropePinHole;

        [Tooltip("Cierre de mandíbulas a partir del cual el instrumento sutura. " +
                 "Equivale al Buttons[0] del stylus en el proyecto original.")]
        [Range(0f, 1f)] public float graspThreshold = 0.72f;

        [Tooltip("Fuerza devuelta al atravesar tejido, en newtons de mundo. " +
                 "En Quest se siente como vibración; en Touch, como fuerza real.")]
        public float punctureForce = 1.2f;

        [Tooltip("Aguja del ejercicio. Sujétala con la pinza: es SU PUNTA la " +
                 "que perfora los puntos, como en SuturingSimulator.")]
        public SutureNeedle needle;

        /// <summary>La pinza de ESTE brazo tiene la aguja agarrada.</summary>
        public bool HoldingNeedle =>
            needle != null && needle.Grasp != null &&
            needle.Grasp.IsHeld && needle.Grasp.Holder == arm;

        [Tooltip("Renderer de la parte visible de la pinza. La distancia se mide " +
                 "contra SU SUPERFICIE, no contra un punto teórico.")]
        public Renderer toolRenderer;

        [Tooltip("Holgura desde la superficie visible de la pinza hasta el punto, " +
                 "en metros. Al medir contra la superficie y no contra un ancla, " +
                 "este número significa de verdad 'cuánto puedo fallar'.")]
        public float captureRadius = 0.015f;

        [Tooltip("Escribe en consola cada captura, y al cerrar en vano informa a " +
                 "qué distancia quedó el punto más cercano.")]
        public bool logCaptures = true;

        [Tooltip("Imprime el estado de entrada una vez por segundo. Sirve para " +
                 "distinguir 'no llega el control' de 'no alcanzo el punto'. " +
                 "Apagado ahora que el rig funciona; enciéndelo si algo falla.")]
        public bool logDiagnostics = false;

        bool _wasTightening;
        bool _hiloAtado;
        bool _wasClosed;
        float _nextStatus;

        void Reset()
        {
            arm = GetComponentInParent<RoboticArm>();
            EnsurePhysics();
        }

        void Awake() { EnsurePhysics(); }

        /// <summary>
        /// Unity solo emite eventos de trigger si AL MENOS UNO de los dos
        /// colliders tiene Rigidbody. Los SuturePoint son triggers sin
        /// Rigidbody, así que el proxy debe aportarlo: cinemático y sin
        /// gravedad, igual que el "Proxy Aguja" del proyecto original.
        /// Sin esto no se dispara absolutamente nada.
        /// </summary>
        void EnsurePhysics()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = false;   // el trigger lo pone el SuturePoint

            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            // el proxy se teletransporta a la punta cada frame; sin esto puede
            // atravesar marcadores pequeños sin registrarlos
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        void LateUpdate()
        {
            if (arm == null) return;

            // El proxy sigue la punta del instrumento teleoperado.
            transform.position = arm.TipPos;

            AtarHiloALaAguja();

            // Tensar la sutura. En el original era Buttons[1] del stylus;
            // MasterInput.Secondary ya unifica B/Y del Quest y el botón trasero
            // del Touch, así que aquí no hay que distinguir el dispositivo.
            var master = arm.master;
            bool tighten = master != null && master.Secondary;
            if (tighten && !_wasTightening && sutureManager != null)
                sutureManager.TightenSuture();
            _wasTightening = tighten;

            TryCapture();
            ReportStatus();
        }

        /// <summary>
        /// Detección por consulta explícita en vez de por callbacks de trigger.
        ///
        /// Los callbacks resultaron poco fiables aquí: OnTriggerStay NO se emite
        /// si el Rigidbody está dormido, y un rigidbody cinemático que se
        /// teletransporta con transform.position no se despierta de forma
        /// fiable. Physics.OverlapSphere no depende de nada de eso, y es
        /// exactamente lo que ya usa RoboticArm.UpdateGrasp en este proyecto
        /// para agarrar Graspables, así que es un camino probado.
        /// </summary>
        void TryCapture()
        {
            bool closed = arm.JawCloseness >= graspThreshold;
            if (!closed)
            {
                _wasClosed = false;
                return;
            }

            // Se recorren TODOS los puntos y se mide contra la superficie
            // visible de la pinza. Son 11 puntos: el coste es irrelevante y a
            // cambio desaparece toda dependencia de colliders y de anclas.
            SuturePoint best = null;
            float bestDist = float.MaxValue;
            foreach (var sp in FindObjectsByType<SuturePoint>(FindObjectsSortMode.None))
            {
                if (sp.used) continue;
                // Solo puntos de un par, y la salida después de su entrada.
                // Sin esto se podía capturar SuturePoint_0b (no está en ningún
                // par) y descolocar toda la secuencia de objetivos.
                if (OrganRig.I != null && !OrganRig.I.CanSuture(sp)) continue;
                float d = DistanceToTool(sp.transform.position);
                if (d > captureRadius) continue;
                if (d < bestDist) { bestDist = d; best = sp; }
            }

            if (best != null)
            {
                Capture(best, bestDist);
            }
            else if (!_wasClosed && logCaptures)
            {
                // Solo en el flanco de cierre, para no inundar la consola.
                ReportNearest();
            }

            _wasClosed = true;
        }


        /// <summary>
        /// Estado de entrada, una vez por segundo, PASE LO QUE PASE.
        ///
        /// El diagnóstico anterior solo saltaba con el gatillo cerrado, así que
        /// si la entrada XR no llega (sin visor y sin XR Device Simulator, el
        /// caso habitual al darle a Play en el editor) JawCloseness se queda en
        /// 0 y no se imprimía absolutamente nada: parecía que fallaba la
        /// captura cuando en realidad no llegaba ni el movimiento.
        /// </summary>
        void ReportStatus()
        {
            if (!logDiagnostics || Time.time < _nextStatus) return;
            _nextStatus = Time.time + 1f;

            var master = arm.master;
            string entrada = master == null
                ? "SIN MasterInput"
                : $"backend={master.ActiveBackend} tracked={master.IsTracked} " +
                  $"gatillo={master.Trigger:F2} aguja={(HoldingNeedle ? "EN MANO" : "suelta")}";

            var all = FindObjectsByType<SuturePoint>(FindObjectsSortMode.None);
            float best = float.MaxValue;
            string nearest = "ninguno";
            int libres = 0;
            foreach (var sp in all)
            {
                if (!sp.used) libres++;
                float d = DistanceToTool(sp.transform.position);
                if (d < best) { best = d; nearest = sp.name; }
            }

            Debug.Log($"[SutureRig] {entrada} | umbral={graspThreshold:F2} " +
                      $"| punta={arm.TipPos} | más cercano {nearest} a " +
                      $"{(all.Length == 0 ? -1f : best * 1000f):F0} mm " +
                      $"(holgura {captureRadius * 1000f:F0} mm) | libres {libres}/{all.Length}");
        }


        /// <summary>
        /// Distancia desde un punto hasta la SUPERFICIE VISIBLE de la pinza.
        ///
        /// Medir contra un ancla teórica falló tres veces seguidas: cada vez que
        /// deducía dónde estaban las mandíbulas por geometría del modelo, el
        /// número salía distinto de lo que se ve en el visor. Contra los bounds
        /// del renderer no hay nada que deducir: si la pinza toca el marcador,
        /// la distancia es cero.
        /// </summary>
        float DistanceToTool(Vector3 worldPoint)
        {
            // Con la aguja en la mano, PERFORA LA AGUJA, no la pinza. Es la
            // mecánica de SuturingSimulator: allí el "Proxy Aguja" era el que
            // disparaba PinSuturePoint, y la pinza solo lo sujetaba.
            if (HoldingNeedle)
                return Vector3.Distance(needle.TipPos, worldPoint);

            // Sin aguja se admite tocar con la pinza, para poder probar el rig
            // sin tener que agarrarla primero.
            if (toolRenderer != null)
                return Vector3.Distance(toolRenderer.bounds.ClosestPoint(worldPoint), worldPoint);

            return Vector3.Distance(arm.TipPos, worldPoint);
        }

        /// <summary>
        /// Ata el nodo 0 del hilo Verlet a la cola de la aguja, una vez.
        ///
        /// En la escena el hilo no tenía extremo atado (connectedBodyStart y
        /// initialPinPoints vacíos): nadie tiraba de él, el pin del primer
        /// punto nunca se deslizaba y BoyRopePinHole ignoraba en silencio
        /// todos los puntos siguientes. Se espera a que el hilo exista
        /// (VerletRopeGenerator lo crea en su Start).
        /// </summary>
        void AtarHiloALaAguja()
        {
            if (_hiloAtado || ropePinHole == null || needle == null || needle.Tail == null) return;
            var rope = ropePinHole.verletRopeGenerator;
            if (rope == null || rope.Nodes == null || rope.Nodes.Count < 2) return;

            // Rehacer el hilo PARTIENDO de la aguja y con la longitud que pide la
            // sutura completa (10 puntos en zigzag ~35 cm); el de la escena
            // medía 30 cm y nacía a 17 cm de la aguja, así que el primer
            // segmento salía estirado desde el principio.
            // Se tiende sobre la mesa en diagonal hacia delante-izquierda, por
            // encima del órgano: tendido hacia fuera se salía por el borde y
            // 40 cm de hilo colgando estiraban el primer tramo.
            Vector3 a = needle.Tail.position;
            var campo = GameObject.Find("SurgicalField");
            Vector3 destino = (campo != null ? campo.transform.position : a) + new Vector3(-0.30f, 0f, -0.22f);
            Vector3 dir = destino - a; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.right;
            rope.stiffness = ropeStiffness;
            rope.constraintIterations = Mathf.Max(rope.constraintIterations, ropeIterations);
            rope.ropeResolution = ropeResolution;
            rope.ropeStartPoint = a;
            rope.ropeEndPoint = a + dir * ropeLength;
            rope.InitializeRope();

            rope.PinNode(0, needle.Tail, Vector3.zero);
            ropePinHole.slideThreshold = rope.SegmentLength * slideThresholdSegments;
            _hiloAtado = true;
            ConfigurarFisicaHilo(rope);
            if (logCaptures) Debug.Log("[SutureRig] Hilo atado a la cola de la aguja.");
        }

        [Header("Física del hilo")]
        [Tooltip("Rigidez del solver del hilo (0-1). El asset venía con 0.2: con " +
                 "61 segmentos no converge y el hilo colgado se estiraba ~15x.")]
        [Range(0.05f, 1f)] public float ropeStiffness = 1f;

        [Tooltip("Longitud del hilo en metros. La sutura completa de 5 pares " +
                 "consume ~35 cm más el cabo hasta la aguja.")]
        public float ropeLength = 0.9f;

        [Tooltip("Cuántos segmentos puede estirarse el tramo junto a un punto " +
                 "antes de que el hilo corra por el agujero. El 0.047 m de la " +
                 "escena era ~6 segmentos y el hilo junto a la aguja se veía " +
                 "como una línea recta muy estirada.")]
        public float slideThresholdSegments = 2.5f;

        [Tooltip("Iteraciones del solver del hilo (mínimo).")]
        public int ropeIterations = 32;

        [Tooltip("Resolución del hilo (0-1). 0.3 con 60 cm da ~73 nodos de 8 mm: " +
                 "suficiente para la sutura y barato de simular.")]
        [Range(0.05f, 1f)] public float ropeResolution = 0.3f;

        [Tooltip("Capa (sin nombre) con la que choca el hilo: superficie del " +
                 "hígado y mesa. Separada para que el hilo no empuje la aguja ni " +
                 "las pinzas.")]
        [Range(8, 31)] public int ropeCollisionLayer = 30;

        /// <summary>
        /// El hilo no chocaba con nada (collisionLayer = Nothing): atravesaba el
        /// hígado y la mesa y quedaba colgando, y con rigidez 0.2 el solver no
        /// sostenía su propio peso. Se le da rigidez completa y una capa propia
        /// con un collider del hígado (forma inicial) y otro de la mesa.
        /// </summary>
        void ConfigurarFisicaHilo(AdvancedRope.VerletRopeGenerator rope)
        {

            var organo = OrganRig.I != null ? OrganRig.I.deformable : null;
            if (organo != null && organo.meshFilter != null)
            {
                var go = new GameObject("ColisionHilo_Higado") { layer = ropeCollisionLayer };
                go.transform.SetParent(organo.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = organo.meshFilter.sharedMesh;
            }

            var mesa = GameObject.Find("FieldBase");
            if (mesa != null)
            {
                var go = new GameObject("ColisionHilo_Mesa") { layer = ropeCollisionLayer };
                go.transform.SetParent(mesa.transform, false);
                go.AddComponent<BoxCollider>();   // cubo unitario = el de la mesa
            }

            rope.collisionLayer = 1 << ropeCollisionLayer;
            rope.enableCollisions = true;
        }

        void Capture(SuturePoint suturePoint, float dist)
        {
            // Una sola vía para suturar, compartida con los niveles.
            if (OrganRig.I != null) { if (!OrganRig.I.Suture(suturePoint)) return; }
            else suturePoint.used = true;

            if (logCaptures)
                Debug.Log($"[SutureRig] Punto capturado: {suturePoint.name} " +
                          $"(vertexIndex={suturePoint.vertexIndex}, a {dist * 1000f:F1} mm)");

            // Realimentación por el mismo canal que el resto del entrenador:
            // vibración en Quest, fuerza real en Touch. Sin ramas por dispositivo.
            var master = arm.master;
            if (master != null)
            {
                master.Haptic(0.7f, 0.08f);
                master.SendForce(-transform.forward * punctureForce);
            }
        }

        /// <summary>
        /// Diagnóstico: al cerrar sin capturar nada, dice a qué distancia quedó
        /// el punto más cercano. Distingue "no llego" de "el radio es corto".
        /// </summary>
        void ReportNearest()
        {
            var all = FindObjectsByType<SuturePoint>(FindObjectsSortMode.None);
            if (all.Length == 0)
            {
                Debug.LogWarning("[SutureRig] No hay ningún SuturePoint en la escena.");
                return;
            }

            SuturePoint nearest = null;
            float best = float.MaxValue;
            int libres = 0;
            foreach (var sp in all)
            {
                if (!sp.used) libres++;
                float d = DistanceToTool(sp.transform.position);
                if (d < best) { best = d; nearest = sp; }
            }

            Debug.Log($"[SutureRig] Gatillo cerrado ({arm.JawCloseness:F2}) pero sin captura. " +
                      $"Más cercano: {nearest.name} a {best * 1000f:F0} mm " +
                      $"(holgura {captureRadius * 1000f:F0} mm desde la pinza). " +
                      $"Puntos libres: {libres}/{all.Length}.");
        }
    }
}
