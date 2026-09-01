using System.Collections.Generic;
using AdvancedRope;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Hilo de sutura sobre el asset AdvancedRope, cumpliendo el contrato de
    /// <see cref="ThreadBase"/> para que los cinco niveles lo usen sin cambios.
    ///
    /// Existe para que haya UN solo hilo en el proyecto. Hasta ahora había dos:
    /// los niveles creaban un <see cref="VerletThread"/> propio y el banco de
    /// pruebas (ST_06) montaba un VerletRopeGenerator del asset, así que la
    /// escena donde de verdad se sutura usaba el hilo casero y la que tenía el
    /// bueno no tenía mecánica. Se registra en <see cref="ThreadBootstrap"/>.
    ///
    /// El desajuste que hay que salvar: VerletThread clava pines en POSICIONES
    /// de mundo y AdvancedRope pinea nodos contra TRANSFORMS. Aquí cada pin
    /// lleva su propio objeto ancla, que además es lo que permite cinchar
    /// (mover el ancla arrastra el nodo).
    /// </summary>
    // Antes que VerletRopeGenerator (orden 0): su LateUpdate reconstruye la
    // malla del hilo, y la corrección que lo apoya sobre el tejido tiene que
    // estar aplicada ya cuando eso ocurre.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class AdvancedRopeThread : ThreadBase
    {
        [Tooltip("Largo del cabo en metros del campo magnificado. Mismo valor " +
                 "que usaba VerletThread, para que los umbrales de los niveles " +
                 "sigan significando lo mismo.")]
        public float totalLength = 1.1f;

        [Tooltip("Radio del hilo. 1.25 mm de radio = 2.5 mm de diámetro, el " +
                 "mismo calibre visible que el resto del proyecto.")]
        public float radius = 0.00125f;

        [Tooltip("Nodos del cabo. 70 reproduce la discretización de " +
                 "VerletThread: con 1.1 m salen segmentos de ~16 mm, que es la " +
                 "unidad en la que los niveles miden el paso de hilo.")]
        public int nodeCount = 70;

        public Material normalMat;
        public Material tenseMat;   // umbral de tensión: en ThreadBase

        [Tooltip("Colisión del hilo contra la escena. Apagada por defecto: " +
                 "VerletThread no colisionaba, y encenderla cambia el " +
                 "comportamiento de los ejercicios ya calibrados.")]
        public bool collide = false;

        [Tooltip("A qué ritmo desliza el hilo por los agujeros al tirar, en " +
                 "segmentos por segundo. 10 son unos 16 cm/s de hilo, que es " +
                 "un tirón de sutura creíble.")]
        public float slideSegmentsPerSecond = 10f;

        [Tooltip("Impide que el hilo se hunda en el tejido. Sin esto el cabo " +
                 "cae dentro del órgano y el punto parece cortado de golpe.")]
        public bool restOnTissue = true;

        public override float Tension => _tension;
        public override bool HasPins => pins.Count > 0;

        public override int RemainingTailSegments =>
            pins.Count > 0 ? (NodeTotal - 1 - pins[pins.Count - 1].index) : NodeTotal - 1;

        public override Vector3 TailParticlePos =>
            Ready ? rope.Nodes[NodeTotal - 1].position : transform.position;

        public override Vector3 ParticleAt(float t01) =>
            Ready
                ? rope.Nodes[Mathf.Clamp(Mathf.RoundToInt(t01 * (NodeTotal - 1)), 0, NodeTotal - 1)].position
                : transform.position;

        class Pin
        {
            public int index;
            public Transform anchor;   // objeto que arrastra el nodo
        }

        readonly List<Pin> pins = new List<Pin>();

        VerletRopeGenerator rope;
        MeshRenderer mr;
        Transform anchorHolder;        // padre de las anclas de pin
        float segLen;
        float _tension;
        float lastTensionEvent = -10f;
        bool tenseVisual;
        bool anchored;
        float _slideDebt;        // fracción de segmento pendiente de deslizar
        bool _avisadoSinCabo;
        TissuePatch _tejido;
        float _proximaBusquedaTejido;

        bool Ready => rope != null && rope.Nodes != null && rope.Nodes.Count > 1;
        int NodeTotal => rope.Nodes.Count;

        // ------------------------------------------------------------------
        void Awake()
        {
            mr = GetComponent<MeshRenderer>();

            if (normalMat == null && MaterialSet.I != null) normalMat = MaterialSet.I.thread;
            if (tenseMat == null && MaterialSet.I != null) tenseMat = MaterialSet.I.threadTense;
            if (normalMat != null) mr.sharedMaterial = normalMat;

            anchorHolder = new GameObject("PinAnchors").transform;
            anchorHolder.SetParent(transform, false);

            rope = gameObject.AddComponent<VerletRopeGenerator>();
            rope.initializeOnStart = false;   // se inicializa aquí, con el ancla ya puesta
            rope.ropeRadius = radius;
            rope.gravity = new Vector3(0f, -1.2f, 0f);
            rope.airFriction = 1f;
            rope.stiffness = 1f;
            rope.constraintIterations = 12;
            rope.enableCollisions = collide;
            rope.enableSelfCollision = false;
            rope.meshRadialSegments = 6;
            rope.meshTextureTiling = 1f;
            rope.initialPinPoints = new List<VerletRopeGenerator.PinConfiguration>();
        }

        void Start()
        {
            // El nivel asigna `anchor` DESPUÉS del Awake (TrainingLevel.SpawnThread),
            // así que el cabo no puede nacer hasta aquí o saldría en el origen.
            Vector3 a = anchor != null ? anchor.position : transform.position;

            // AdvancedRope deriva el número de nodos de
            // lerp(2, distancia/diámetro, ropeResolution). Se despeja la
            // resolución que da el nodeCount pedido, en vez de dejarla al azar:
            // los niveles cuentan SEGMENTOS para medir el paso de hilo, así que
            // cuántos haya es parte del contrato.
            float maxNodes = Mathf.Floor(totalLength / Mathf.Max(0.001f, radius * 2f));
            rope.ropeResolution = maxNodes > 2f
                ? Mathf.Clamp01((nodeCount - 2f) / (maxNodes - 2f))
                : 1f;

            rope.ropeStartPoint = a;
            rope.ropeEndPoint = a + Vector3.down * totalLength;
            rope.InitializeRope();

            segLen = totalLength / Mathf.Max(1, NodeTotal - 1);

            if (normalMat != null) mr.sharedMaterial = normalMat;
        }

        void FixedUpdate()
        {
            if (!Ready) return;

            // El ancla es la cola de la aguja, que se mueve cada frame. Pinear
            // el nodo 0 contra ese Transform una sola vez basta: AdvancedRope
            // reevalúa la posición del pin en su propia simulación.
            if (!anchored && anchor != null)
            {
                rope.PinNode(0, anchor);
                anchored = true;
            }
            if (tailHolder != null) rope.PinNode(NodeTotal - 1, tailHolder);

            ComputeTension();
            SlideThroughPins(Time.fixedDeltaTime);
        }

        /// <summary>
        /// Deja el hilo APOYADO sobre el tejido en vez de dentro.
        ///
        /// El hilo no colisiona (collide = false, heredado de VerletThread para
        /// no cambiar ejercicios calibrados), así que la cola caía por gravedad
        /// atravesando el órgano y se perdía dentro de la malla. En pantalla el
        /// hilo llegaba al punto y se acababa ahí de golpe: ni lazo, ni cabo,
        /// ni nada. Eso es lo que se veía "atorado" al suturar.
        ///
        /// Se resuelve con el mismo plano lógico que ya usa la háptica
        /// (TissuePatch: centro de la herida y su normal), que es mucho más
        /// barato y estable que colisionar 70 nodos contra 4735 vértices
        /// deformables.
        ///
        /// Va en LateUpdate, y por eso la clase declara un orden de ejecución
        /// adelantado: el rope reconstruye su malla en SU LateUpdate, así que
        /// hay que corregir los nodos antes de que la dibuje.
        /// </summary>
        void LateUpdate()
        {
            if (!Ready || !restOnTissue) return;

            if (_tejido == null)
            {
                if (Time.time < _proximaBusquedaTejido) return;
                _proximaBusquedaTejido = Time.time + 0.5f;
                _tejido = FindFirstObjectByType<TissuePatch>();
                if (_tejido == null) return;
            }

            Transform t = _tejido.transform;
            Vector3 n = t.up;
            Vector3 centro = _tejido.WoundCenter;
            float semiAncho = _tejido.width * 0.5f;
            float semiFondo = _tejido.depth * 0.5f;

            var nodos = rope.Nodes;
            for (int i = 0; i < nodos.Count; i++)
            {
                var nodo = nodos[i];

                // Los pines mandan: son los agujeros del tejido y están donde
                // tienen que estar, aunque sea justo en la superficie.
                if (nodo.isPinned) continue;

                float altura = Vector3.Dot(nodo.position - centro, n);
                if (altura >= radius) continue;

                // Solo sobre la almohadilla; fuera de ella no hay tejido que
                // sostenga nada y el hilo debe poder caer.
                Vector3 local = t.InverseTransformPoint(nodo.position);
                if (Mathf.Abs(local.x) > semiAncho || Mathf.Abs(local.z) > semiFondo) continue;

                // Se mueve TAMBIÉN prevPosition: en Verlet la velocidad es la
                // diferencia entre ambas, así que corregir solo position
                // inyectaría un impulso y el hilo saldría disparado.
                Vector3 correccion = n * (radius - altura);
                nodo.position += correccion;
                nodo.prevPosition += correccion;
            }
        }

        // ------------------------------------------------------------------
        void ComputeTension()
        {
            // Se mide contra el ÚLTIMO pin, no contra el primero.
            //
            // VerletThread usaba pins[0] y es un error que solo se nota a
            // partir del segundo punto: pins[0] es el primer agujero que se
            // hizo nunca, y ese tramo ya no se toca. La tensión se quedaba
            // midiendo una distancia que nadie estaba tirando, y en la sesión
            // del 01-09 se ve congelada en 1.4288 durante segundos.
            //
            // El tramo que de verdad se tensa es el que va de la aguja al
            // último agujero. Además así se cierra el lazo: al tirar sube la
            // tensión, el hilo desliza, el tramo en reposo se alarga y la
            // tensión baja. Es lo que hace un hilo pasando por un ojal.
            int end = pins.Count > 0
                ? pins[pins.Count - 1].index
                : (tailHolder != null ? NodeTotal - 1 : -1);

            if (end <= 0) { _tension = 0f; SetTenseVisual(false); return; }

            float direct = Vector3.Distance(rope.Nodes[0].position, rope.Nodes[end].position);
            _tension = direct / Mathf.Max(segLen * end, 1e-4f);

            bool tense = _tension > tenseThreshold;
            SetTenseVisual(tense);

            if (tense && Time.time - lastTensionEvent > 1.2f)
            {
                lastTensionEvent = Time.time;
                onTensionEvent?.Invoke();
            }
        }

        void SetTenseVisual(bool tense)
        {
            if (tense == tenseVisual) return;
            tenseVisual = tense;
            if (mr != null && normalMat != null && tenseMat != null)
                mr.sharedMaterial = tense ? tenseMat : normalMat;
        }

        /// <summary>
        /// El hilo desliza por el último par de pines al tirar (pull-through).
        /// Es lo que hace que RemainingTailSegments baje y el nivel dé el punto
        /// por pasado, así que tiene que comportarse igual que en VerletThread.
        /// </summary>
        void SlideThroughPins(float dt)
        {
            if (pins.Count < 2 || _tension < 1.05f) { _slideDebt = 0f; return; }

            var last = pins[pins.Count - 1];
            var prev = pins[pins.Count - 2];

            if (last.index >= NodeTotal - 4)
            {
                if (!_avisadoSinCabo)
                {
                    _avisadoSinCabo = true;
                    Debug.LogWarning("[Hilo] Se acabó el cabo: no queda hilo que pasar " +
                                     "por el último punto. Sube nodeCount o totalLength " +
                                     "si el ejercicio necesita más puntos.");
                }
                return;
            }

            // A RITMO, no un índice por FixedUpdate.
            //
            // Sin límite esto avanzaba 50 índices por segundo —a 16 mm por
            // segmento, 80 cm de hilo por segundo—, así que consumía los 70
            // nodos del cabo en 1.3 s. A partir de ahí el hilo se quedaba
            // clavado entre la aguja y un pin fijo, estirado, y se veía como
            // una línea recta tirante en vez de un cabo. Eso es el "atorado".
            _slideDebt += slideSegmentsPerSecond * dt;
            if (_slideDebt < 1f) return;

            int pasos = Mathf.FloorToInt(_slideDebt);
            _slideDebt -= pasos;

            pasos = Mathf.Min(pasos, NodeTotal - 4 - last.index);
            if (pasos <= 0) return;

            // El de más atrás primero: si se moviera antes el último, al
            // repinar el anterior se desharía el pin recién puesto cuando
            // quedan pegados.
            Repin(prev, prev.index + pasos);
            Repin(last, last.index + pasos);
        }

        /// <summary>Mueve un pin de nodo: suelta el viejo y ata el nuevo al mismo ancla.</summary>
        void Repin(Pin pin, int newIndex)
        {
            rope.UnpinNode(pin.index);
            pin.index = Mathf.Clamp(newIndex, 1, NodeTotal - 2);
            rope.PinNode(pin.index, pin.anchor);
        }

        // ------------------------------------------------------------------
        public override void PinThroughStitch(Vector3 exitHole, Vector3 entryHole)
        {
            if (!Ready) return;

            int i1 = NearestFreeNode(exitHole);
            int i2 = Mathf.Min(i1 + 3, NodeTotal - 2);

            AddPin(i1, exitHole);
            AddPin(i2, entryHole);
        }

        void AddPin(int index, Vector3 worldPos)
        {
            var a = new GameObject($"Pin_{pins.Count}").transform;
            a.SetParent(anchorHolder, false);
            a.position = worldPos;

            var pin = new Pin { index = index, anchor = a };
            pins.Add(pin);
            rope.PinNode(pin.index, a);
        }

        int NearestFreeNode(Vector3 pos)
        {
            int minIdx = 2;
            foreach (var pin in pins) minIdx = Mathf.Max(minIdx, pin.index + 2);

            int best = Mathf.Min(minIdx, NodeTotal - 5);
            float bestD = float.MaxValue;
            for (int i = minIdx; i < NodeTotal - 4; i++)
            {
                float d = Vector3.SqrMagnitude(rope.Nodes[i].position - pos);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Cincha el último punto: se mueven las ANCLAS, y los nodos las siguen.
        /// </summary>
        public override void CinchTo(Vector3 point)
        {
            int start = Mathf.Max(0, pins.Count - 2);   // solo el último par
            for (int i = start; i < pins.Count; i++)
                if (pins[i].anchor != null)
                    pins[i].anchor.position = point + Random.insideUnitSphere * 0.004f;
        }

        public override void ClearPins()
        {
            foreach (var pin in pins)
            {
                if (Ready) rope.UnpinNode(pin.index);
                if (pin.anchor != null) Destroy(pin.anchor.gameObject);
            }
            pins.Clear();

            // Reintentar un nivel tiene que empezar con el cabo entero.
            _slideDebt = 0f;
            _avisadoSinCabo = false;
        }
    }
}
