using System.Collections.Generic;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Puente entre el subsistema de sutura portado de SuturingSimulator y los
    /// niveles de este proyecto.
    ///
    /// Los niveles siguen mandando: conservan sus fases, sus métricas y su
    /// puntuación. Lo único que cambia es que sus dianas se colocan sobre la
    /// incisión REAL del órgano en vez de sobre un parche plano, y que al
    /// perforar se dispara la sutura de verdad (pin del hilo + deformación).
    ///
    /// Los SuturePoint alternan entre los dos labios de la herida, así que se
    /// emparejan solos: labio alto = entrada, labio bajo = salida.
    /// </summary>
    public class OrganRig : MonoBehaviour
    {
        public static OrganRig I { get; private set; }

        [System.Serializable]
        public class StitchPair
        {
            public SuturePoint entry;   // labio alto
            public SuturePoint exit;    // labio bajo
        }

        public SutureManager sutureManager;
        public BoyRopePinHole ropePinHole;
        public DeformableMesh deformable;

        [Tooltip("Pares entrada/salida a lo largo de la incisión, ordenados.")]
        public List<StitchPair> pairs = new List<StitchPair>();

        [Tooltip("Color del marcador ya suturado.")]
        public Color capturedColor = new Color(0.2f, 0.9f, 0.3f);

        public int PairCount => pairs.Count;

        [Header("Apertura inicial de la herida")]
        [Tooltip("Separación de los labios al empezar, en metros.\n\n" +
                 "El modelo del hígado tiene la malla CONTINUA: el corte solo " +
                 "está pintado en tex_higado_cortado, así que sin esto la " +
                 "herida nace visualmente cerrada y suturar no tiene nada que " +
                 "cerrar. Aquí se separan los labios de verdad para que la " +
                 "sutura pueda aproximarlos. 0 = dejar la malla como viene.")]
        public float woundOpening = 0.012f;

        [Tooltip("Hasta dónde llega la apertura a cada lado de la línea de corte.")]
        public float openingFalloff = 0.016f;

        [Range(0f, 1f)]
        [Tooltip("Cuánto se aproximan los labios en cada punto suturado.\n\n" +
                 "0 = el punto se queda donde está (lo que hacía el original: " +
                 "la herida no cerraba). 0.5 = los dos labios del par se " +
                 "encuentran en el centro. 1 = cada labio va hasta el opuesto, " +
                 "que se pasa de frenada y los cruza.")]
        public float approximation = 0.5f;

        bool _abierta;

        /// <summary>
        /// Puntos ya suturados: índice de su objetivo en
        /// SutureManager.sutureVertices y posición del labio al suturarlo.
        /// Hace falta para aproximar cada punto a SU PAREJA al tensar, en vez
        /// de a sus vecinos de la lista (que es lo que abría la herida).
        /// </summary>
        readonly Dictionary<SuturePoint, (int idx, Vector3 inicio)> _suturados =
            new Dictionary<SuturePoint, (int idx, Vector3 inicio)>();

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void LateUpdate()
        {
            // DeformableMesh construye masterVertices en su Start; se espera a
            // que exista en vez de depender del orden de ejecución.
            if (_abierta || woundOpening <= 0f) return;
            if (deformable == null || deformable.masterVertices == null ||
                deformable.masterVertices.Count == 0) return;

            AbrirHerida();
            _abierta = true;
        }

        /// <summary>
        /// Separa los labios desplazando los vértices a un lado y otro de la
        /// línea de corte. Se mueve también initialPosition: si solo se moviera
        /// position, la simulación (que tiende al reposo con stiffnessOrigin)
        /// devolvería la herida a su estado cerrado en cuanto arrancara.
        /// </summary>
        public void AbrirHerida()
        {
            if (pairs.Count == 0) return;

            Vector3 centro = WoundCenter;
            Vector3 cruce = (pairs[0].exit.transform.position -
                             pairs[0].entry.transform.position).normalized;

            // quitar la componente paralela a la incisión: queremos el eje que
            // la CRUZA, no el que la recorre
            Vector3 along = WoundDir;
            cruce = (cruce - along * Vector3.Dot(cruce, along)).normalized;
            if (cruce.sqrMagnitude < 0.5f) return;

            int movidos = 0;
            foreach (var mv in deformable.masterVertices)
            {
                if (mv.isFixed) continue;

                float d = Vector3.Dot(mv.initialPosition - centro, cruce);
                float caida = 1f - Mathf.Clamp01(Mathf.Abs(d) / openingFalloff);
                if (caida <= 0f) continue;

                Vector3 empuje = cruce * (Mathf.Sign(d) * woundOpening * 0.5f * caida);
                mv.initialPosition += empuje;
                mv.position += empuje;
                movidos++;
            }

            Debug.Log($"[OrganRig] Herida abierta {woundOpening * 1000f:F0} mm " +
                      $"({movidos} vértices desplazados). Suturar la cerrará.");
        }

        public Vector3 EntryPos(int i) => pairs[i].entry.transform.position;
        public Vector3 ExitPos(int i) => pairs[i].exit.transform.position;

        /// <summary>Centro de la incisión, para orientar el tejido lógico.</summary>
        public Vector3 WoundCenter
        {
            get
            {
                if (pairs.Count == 0) return transform.position;
                Vector3 sum = Vector3.zero;
                foreach (var p in pairs) sum += (p.entry.transform.position + p.exit.transform.position) * 0.5f;
                return sum / pairs.Count;
            }
        }

        /// <summary>Dirección a lo largo de la que corre la incisión.</summary>
        public Vector3 WoundDir
        {
            get
            {
                if (pairs.Count < 2) return Vector3.right;
                Vector3 a = (pairs[0].entry.transform.position + pairs[0].exit.transform.position) * 0.5f;
                int last = pairs.Count - 1;
                Vector3 b = (pairs[last].entry.transform.position + pairs[last].exit.transform.position) * 0.5f;
                Vector3 d = b - a;
                d.y = 0f;
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right;
            }
        }

        /// <summary>
        /// Sutura de verdad ese punto: fija el hilo y deforma el tejido.
        /// Es el único sitio donde se hace, para que valga igual si lo dispara
        /// un nivel o el <see cref="SutureRig"/> en la escena de pruebas.
        /// </summary>
        public bool Suture(SuturePoint sp)
        {
            if (sp == null || sp.used) return false;

            sp.used = true;
            if (sutureManager != null && sutureManager.sutureVertices != null)
                _suturados[sp] = (sutureManager.sutureVertices.Count, sp.transform.position);
            // El hilo Verlet de AdvancedRope solo existe en el banco de pruebas;
            // dentro de un nivel el hilo visible es el del propio nivel.
            if (ropePinHole != null) ropePinHole.PinNode(sp);
            if (sutureManager != null) sutureManager.SutureVertices(sp, TargetFor(sp));
            Tint(sp);
            return true;
        }

        /// <summary>
        /// A dónde tira un punto al suturarlo: hacia su pareja del labio de
        /// enfrente, que es lo que hace un punto de sutura de verdad.
        ///
        /// SuturingSimulator fijaba el objetivo en la posición ACTUAL del punto,
        /// así que suturar lo clavaba donde ya estaba y la herida no se cerraba
        /// nunca; la única aproximación venía de TightenSuture(), que empuja
        /// cada objetivo hacia sus vecinos EN LA LISTA. Si no se suturaba
        /// alternando labios, eso arrastraba a lo largo de la incisión en vez
        /// de cruzarla, y el hueco se quedaba igual o se abría más.
        ///
        /// No se pone el objetivo en el labio contrario sino a medio camino:
        /// los dos puntos del par tiran del mismo sitio y se encuentran en el
        /// centro, sin que ninguno cruce al otro lado.
        /// </summary>
        Vector3 TargetFor(SuturePoint sp)
        {
            foreach (var p in pairs)
            {
                if (p.entry == sp && p.exit != null)
                    return Vector3.Lerp(sp.transform.position,
                                        p.exit.transform.position, approximation);
                if (p.exit == sp && p.entry != null)
                    return Vector3.Lerp(sp.transform.position,
                                        p.entry.transform.position, approximation);
            }
            return sp.transform.position;
        }

        /// <summary>
        /// Aproxima los labios de la herida en los puntos ya suturados.
        ///
        /// En SuturingSimulator esto colgaba de un botón del stylus, sin nada
        /// que explicara cuándo pulsarlo: la herida se cerraba "sola" y a
        /// destiempo. Aquí lo llama el nivel al terminar cada punto, que es
        /// cuando de verdad toca apretar el nudo.
        /// </summary>
        public void Tighten()
        {
            // ---- MODIFICADO ----
            // Antes llamaba a SutureManager.TightenSuture(), que desplaza cada
            // objetivo hacia sus vecinos EN LA LISTA, es decir, a lo largo de
            // la incisión: la simulación con la malla real mostraba que en el
            // nivel 4 dejaba la herida MÁS abierta que sin tensar. Ahora cierra
            // del todo los pares cuyos dos labios ya están suturados.
            ApproximatePairs(0f);
        }

        /// <summary>
        /// ¿Se puede suturar ya este punto? Solo los que forman parte de un par,
        /// y la salida únicamente después de su entrada.
        /// </summary>
        public bool CanSuture(SuturePoint sp)
        {
            if (sp == null || sp.used) return false;
            foreach (var p in pairs)
            {
                if (p.entry == sp) return true;
                if (p.exit == sp) return p.entry == null || p.entry.used;
            }
            return false;   // p. ej. SuturePoint_0b: no pertenece a ningún par
        }

        /// <summary>
        /// Lleva los dos labios de cada par suturado hacia el centro del par.
        /// paso &lt;= 0: cierre completo; paso &gt; 0: avanza como mucho esa
        /// distancia por llamada (tensado manual con B/Y, Espacio o el hilo).
        /// </summary>
        public bool ApproximatePairs(float paso)
        {
            if (sutureManager == null || sutureManager.sutureVertices == null) return false;

            // El labio no llega a su objetivo: se queda en el equilibrio entre
            // la sutura y el resorte al origen, ks/(ks+ko) del camino. Para que
            // alcance el centro, el objetivo se pasa en esa proporción.
            float ks = sutureManager.stiffnessSuture;
            float ko = deformable != null ? deformable.stiffnessOrigin : 0f;
            float sobre = ks > 0f ? (ks + ko) / ks : 1f;

            var lista = sutureManager.sutureVertices;
            bool alguno = false;
            foreach (var p in pairs)
            {
                if (p.entry == null || p.exit == null) continue;
                if (!_suturados.TryGetValue(p.entry, out var a)) continue;
                if (!_suturados.TryGetValue(p.exit, out var b)) continue;

                Vector3 centro = (a.inicio + b.inicio) * 0.5f;
                Mover(lista, a.idx, a.inicio + (centro - a.inicio) * sobre, paso);
                Mover(lista, b.idx, b.inicio + (centro - b.inicio) * sobre, paso);
                alguno = true;
            }
            return alguno;
        }

        static void Mover(List<Vector3> l, int i, Vector3 meta, float paso)
        {
            if (i < 0 || i >= l.Count) return;
            l[i] = paso > 0f ? Vector3.MoveTowards(l[i], meta, paso) : meta;
        }

        /// <summary>Olvida los puntos suturados (al reiniciar la sutura).</summary>
        public void ClearSutures() => _suturados.Clear();

        /// <summary>
        /// Tiñe el marcador. Sin esto un punto suturado se ve idéntico a uno
        /// libre y la acción no se percibe.
        /// </summary>
        void Tint(SuturePoint sp)
        {
            var mr = sp.GetComponentInChildren<MeshRenderer>();
            if (mr == null) return;

            var mat = mr.material;   // instancia: no teñir todos los marcadores
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", capturedColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", capturedColor);
        }

        /// <summary>
        /// Empareja los SuturePoint por altura: se ordenan a lo largo de la
        /// incisión y se agrupa cada punto del labio alto con el del bajo más
        /// cercano. Lo llama el builder al montar la escena.
        /// </summary>
        public void BuildPairs(IList<SuturePoint> points)
        {
            pairs.Clear();
            if (points == null || points.Count == 0) return;

            // Quitar DUPLICADOS antes de nada.
            //
            // El proyecto original trae SuturePoint_0b en la misma posición que
            // SuturePoint_0. Con él dentro, el emparejamiento por vecino más
            // cercano se desplaza una posición y todas las puntadas salvo la
            // primera cruzan la herida EN DIAGONAL (hasta 29 mm de desfase),
            // que es lo que rompía la coherencia del ejercicio.
            const float coincidentes = 0.002f;   // 2 mm
            var unicos = new List<SuturePoint>();
            foreach (var p in points)
            {
                bool repetido = false;
                foreach (var q in unicos)
                {
                    if (Vector3.Distance(p.transform.position, q.transform.position) < coincidentes)
                    {
                        repetido = true;
                        break;
                    }
                }
                if (!repetido) unicos.Add(p);
            }

            float mediaY = 0f;
            foreach (var p in unicos) mediaY += p.transform.position.y;
            mediaY /= unicos.Count;

            var altos = new List<SuturePoint>();
            var bajos = new List<SuturePoint>();
            foreach (var p in unicos)
                (p.transform.position.y >= mediaY ? altos : bajos).Add(p);

            Vector3 dir = Vector3.right;
            if (altos.Count > 1)
            {
                Vector3 d = altos[altos.Count - 1].transform.position - altos[0].transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-6f) dir = d.normalized;
            }
            altos.Sort((a, b) => Vector3.Dot(a.transform.position, dir)
                                  .CompareTo(Vector3.Dot(b.transform.position, dir)));

            var libres = new List<SuturePoint>(bajos);
            foreach (var alto in altos)
            {
                if (libres.Count == 0) break;

                SuturePoint mejor = null;
                float mejorD = float.MaxValue;
                foreach (var bajo in libres)
                {
                    float d = Vector3.Distance(alto.transform.position, bajo.transform.position);
                    if (d < mejorD) { mejorD = d; mejor = bajo; }
                }

                libres.Remove(mejor);
                pairs.Add(new StitchPair { entry = alto, exit = mejor });
            }
        }
    }
}
