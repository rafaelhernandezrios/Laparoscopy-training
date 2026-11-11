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

        [Tooltip("Gravedad del hilo (m/s²). Con 1.2 el tramo en el aire se veía " +
                 "recto como un alambre; 5 da una caída natural sin que el cabo " +
                 "se estire bajo su propio peso.")]
        public float sagGravity = 5f;

        [Tooltip("Segmentos de hilo que ocupa la parte enterrada de una " +
                 "puntada. Con segmentos de 16 mm y labios a ~24 mm, 2 dan " +
                 "32 mm: el sobrante justo para que sea un arco y no un lazo.")]
        public int stitchSpanSegments = 2;

        [Tooltip("Segmentos de hilo entre dos puntos consecutivos. 2 dan " +
                 "32 mm para una separación real de ~24 mm.")]
        public int betweenStitchSegments = 2;

        [Tooltip("Hilo que queda entre la aguja y el primer punto, en " +
                 "segmentos. Solo fija dónde empieza; a partir del segundo " +
                 "punto lo determina cuánto hilo se ha pasado.")]
        public int leadSegments = 8;

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
            // ---- MODIFICADO ----: con -1.2 el hilo apenas caía y entre la aguja
            // y la bandeja se veía como un alambre recto y tenso. Ahora que se
            // apoya en las superficies (ThreadSurfaces) puede pesar de verdad.
            rope.gravity = new Vector3(0f, -sagGravity, 0f);
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

            // ---- MODIFICADO ----
            // Primero las superficies reales (mesa/suelo, bandeja y la malla
            // del hígado, ver ThreadSurfaces). Antes solo había un plano plano
            // sobre el parche: fuera de él —y en las actividades 1-2, que no
            // tienen parche— el hilo caía atravesando la bandeja y la mesa
            // como un alambre recto, y sobre el hígado inclinado flotaba o se
            // hundía según la zona.
            bool algunaSuperficie = false;
            var nodosS = rope.Nodes;
            float holgura = radius + 0.0015f;   // por encima del suelo de la cavidad
            for (int i = 0; i < nodosS.Count; i++)
            {
                var nodo = nodosS[i];
                if (nodo.isPinned || EstaEnterrado(i)) continue;
                if (!ThreadSurfaces.Altura(nodo.position, out float suelo)) continue;
                algunaSuperficie = true;
                float minimo = suelo + holgura;
                if (nodo.position.y >= minimo) continue;
                var correccion = Vector3.up * (minimo - nodo.position.y);
                nodo.position += correccion;
                nodo.prevPosition += correccion;
            }
            if (algunaSuperficie) return;   // el plano lógico queda solo como respaldo

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

                // El tramo ENTRE los dos agujeros de un mismo punto va POR
                // DENTRO del tejido: es la parte enterrada de la puntada.
                // Empujarla hacia fuera la sacaba a la superficie y convertía
                // cada punto en un lazo suelto encima de la herida, que es lo
                // que hacía que la sutura no se leyera como una secuencia.
                if (EstaEnterrado(i)) continue;

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

        /// <summary>
        /// ¿El nodo cae entre los dos agujeros de una misma puntada?
        ///
        /// Los pines van por parejas en orden de colocación: (0,1) es el primer
        /// punto, (2,3) el segundo, y dentro de cada pareja el de menor índice
        /// es la salida —el lado de la aguja— y el mayor la entrada.
        /// </summary>
        bool EstaEnterrado(int idx)
        {
            for (int k = 0; k + 1 < pins.Count; k += 2)
            {
                int a = Mathf.Min(pins[k].index, pins[k + 1].index);
                int b = Mathf.Max(pins[k].index, pins[k + 1].index);
                if (idx > a && idx < b) return true;
            }
            return false;
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
            //
            // "Último agujero" = pin de ÍNDICE MÍNIMO: con los puntos nuevos
            // insertados del lado de la aguja, el más reciente es el de menor
            // índice, no el último de la lista.
            int end;
            if (pins.Count > 0)
            {
                end = int.MaxValue;
                foreach (var pin in pins) end = Mathf.Min(end, pin.index);
            }
            else end = tailHolder != null ? NodeTotal - 1 : -1;

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

            // Tope: el pin más cercano a la cola no puede salirse del cabo.
            int maxIdx = 0;
            foreach (var pin in pins) maxIdx = Mathf.Max(maxIdx, pin.index);

            if (maxIdx >= NodeTotal - 4)
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

            pasos = Mathf.Min(pasos, NodeTotal - 4 - maxIdx);
            if (pasos <= 0) return;

            // TODOS los pines avanzan lo mismo: tirar de la aguja arrastra el
            // hilo por todos los agujeros a la vez, y el trecho de hilo entre
            // agujero y agujero no cambia (es el mismo material). Deslizar
            // solo el último par —lo que hacía VerletThread— acortaba en falso
            // el tramo entre puntos.
            //
            // De mayor índice a menor, para que ningún repin pise el nodo que
            // el siguiente está a punto de dejar libre.
            _ordenados.Clear();
            _ordenados.AddRange(pins);
            _ordenados.Sort((a, b) => b.index.CompareTo(a.index));
            foreach (var pin in _ordenados)
                Repin(pin, pin.index + pasos);
        }
        readonly List<Pin> _ordenados = new List<Pin>();

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

            int i1 = NextStitchNode();
            if (i1 < 0)
            {
                Debug.LogWarning("[Hilo] No queda hilo libre entre la aguja y el último " +
                                 "punto para fijar uno nuevo: tira para pasar más hilo.");
                return;
            }
            int i2 = i1 + Mathf.Max(1, stitchSpanSegments);   // NextStitchNode garantiza que cabe

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

        /// <summary>
        /// Dónde del hilo se hace el próximo punto. Devuelve -1 si no queda
        /// hilo libre entre la aguja y el punto anterior.
        ///
        /// NO se busca el nodo espacialmente más cercano al agujero, que es lo
        /// que se hacía antes y es la causa del enredo. Ese rango abarca toda
        /// la guía entre la aguja y el punto anterior, así que el nodo elegido
        /// dependía de por dónde estuviera flotando el hilo en ese instante: si
        /// caía uno bajo, entre los dos agujeros quedaban 13 segmentos —20 cm
        /// de hilo para 24 mm de separación real— y ese sobrante es el lazo. Y
        /// como cambiaba en cada punto, la maraña crecía a partir del segundo.
        ///
        /// La posición correcta no es geométrica sino de LONGITUD DE ARCO: el
        /// punto nuevo se da con el hilo que hay justo detrás de la aguja, o
        /// sea pegado al punto anterior a lo largo del hilo. Así el trecho
        /// entre puntos es siempre el mismo y no depende del azar.
        ///
        /// El ciclo se sostiene solo: cada punto consume span + separación = 4
        /// nodos hacia abajo, y el tirón que el nivel exige (pullSegments = 5)
        /// vuelve a subir todos los pines, liberando ese hueco para el
        /// siguiente. Si no se ha tirado bastante, no hay sitio y se avisa.
        /// </summary>
        int NextStitchNode()
        {
            int span = Mathf.Max(1, stitchSpanSegments);
            int hueco = Mathf.Max(1, betweenStitchSegments);

            if (pins.Count == 0)
                return Mathf.Clamp(leadSegments, 2, NodeTotal - 2 - span);

            int minPin = int.MaxValue;
            foreach (var pin in pins) minPin = Mathf.Min(minPin, pin.index);

            int i1 = minPin - hueco - span;
            return i1 < 2 ? -1 : i1;
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
