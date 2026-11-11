using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Aguja curva de sutura (media circunferencia). Genera su malla en Awake.
    /// Escala macro 5x: representa una aguja de ~26 mm.
    /// </summary>
    [RequireComponent(typeof(Graspable))]
    public class SutureNeedle : MonoBehaviour
    {
        public float arcRadius = 0.055f;   // radio del arco (macro)
        public float arcDegrees = 180f;
        public float tubeRadius = 0.0035f;
        public Material material;

        public Transform Tip { get; private set; }
        public Transform Tail { get; private set; }
        public Vector3 TipPos => Tip.position;
        public Vector3 TailPos => Tail.position;
        public Vector3 TipDir => Tip.forward;

        public Graspable Grasp { get; private set; }

        [Header("Agarre realista")]
        [Tooltip("Al agarrarla, la aguja se acomoda en las mandíbulas como en un " +
                 "porta-agujas real: sujeta por su cuerpo, con el alambre cruzando " +
                 "entre las mandíbulas y el plano de la aguja perpendicular al " +
                 "instrumento. Así, girar la muñeca hace avanzar la aguja por su curva.")]
        public bool snapToGrip = true;

        [Tooltip("Punto de agarre, en fracción del arco desde la cola (engarce). " +
                 "0.38 = entre el tercio posterior y el medio, lo recomendado.")]
        [Range(0.1f, 0.9f)] public float gripFromTail = 0.38f;

        [Tooltip("Segundos que tarda en acomodarse en las mandíbulas.")]
        public float gripSeconds = 0.15f;

        Coroutine _encaje;

        void Awake()
        {
            Grasp = GetComponent<Graspable>();
            Grasp.onGrabbed += AlAgarrar;

            var mesh = ProcMesh.ArcTube(arcRadius, arcDegrees, tubeRadius,
                out Vector3 tailP, out Vector3 tailT, out Vector3 tipP, out Vector3 tipT);

            var mf = gameObject.GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gameObject.GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            AssignMaterial(mr);

            // Arte importado: se cuelga el modelo real y se apaga el arco
            // procedural, pero la malla, el collider y los transforms Tip/Tail
            // siguen siendo los procedurales, que son los que los niveles miden.
            //
            // Por eso el modelo se CENTRA sobre el arco procedural: si no
            // coinciden, se apunta con lo que se ve y la lógica mide otro punto,
            // y la sutura resulta imposible.
            if (ArtSet.I != null && ArtSet.I.needle != null)
            {
                var art = ArtSet.I;
                Vector3 offset = ArtSet.ComputeAlignOffset(
                    art.needle, art.needleScale, art.needleEuler,
                    mesh.bounds.center) + art.needleOffset;

                var visual = ArtSet.AttachVisual(art.needle, transform,
                                                 art.needleScale, art.needleEuler, offset);
                if (visual != null) mr.enabled = art.showProceduralGuides;
                _visual = visual;
            }

            Tip = new GameObject("Tip").transform;
            Tip.SetParent(transform, false);
            Tip.localPosition = tipP;
            Tip.localRotation = Quaternion.LookRotation(tipT, Vector3.forward);

            Tail = new GameObject("Tail").transform;
            Tail.SetParent(transform, false);
            Tail.localPosition = tailP;

            PegarColaAlModeloVisible();

            var box = gameObject.GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size + Vector3.one * 0.004f;

            var rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.02f;
            rb.isKinematic = true; // se activa la gravedad al soltar (Graspable)
            rb.interpolation = RigidbodyInterpolation.None; // evita retraso visual al ir agarrada
        }

        Transform _visual;

        [Tooltip("Llevar el punto de amarre del hilo al extremo del modelo que " +
                 "se VE, en vez de dejarlo en el del arco procedural.")]
        public bool anclarHiloAlModelo = true;

        /// <summary>
        /// Mueve Tail al extremo real de la aguja visible.
        ///
        /// El modelo importado se alinea sobre el arco procedural por el CENTRO
        /// de sus bounds, no extremo con extremo: comparten el medio, pero cada
        /// uno acaba donde acaba. Así que tailP —el extremo del arco
        /// procedural— no tiene por qué caer sobre la aguja que se ve, y el
        /// hilo salía flotando a un palmo de ella.
        ///
        /// El engarce es, por geometría, el punto de la aguja MÁS LEJANO a la
        /// punta. Eso vale sea cual sea la escala o la orientación del modelo,
        /// que es justo lo que no conviene dar por supuesto: es el mismo tipo
        /// de desajuste que ya obligó a recolocar jawAnchor en el instrumento.
        ///
        /// Tip no se toca: los niveles miden la punción con él y funciona.
        /// </summary>
        void PegarColaAlModeloVisible()
        {
            if (!anclarHiloAlModelo || _visual == null || Tip == null) return;

            Vector3 punta = Tip.position;
            float mejorD = -1f;
            Vector3 mejor = Tail.position;

            foreach (var mf in _visual.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                foreach (var v in m.vertices)
                {
                    Vector3 w = mf.transform.TransformPoint(v);
                    float d = (w - punta).sqrMagnitude;
                    if (d > mejorD) { mejorD = d; mejor = w; }
                }
            }

            foreach (var smr in _visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (m == null) continue;
                foreach (var v in m.vertices)
                {
                    Vector3 w = smr.transform.TransformPoint(v);
                    float d = (w - punta).sqrMagnitude;
                    if (d > mejorD) { mejorD = d; mejor = w; }
                }
            }

            if (mejorD < 0f) return;

            float movido = Vector3.Distance(Tail.position, mejor);
            Tail.position = mejor;
            Debug.Log($"[Aguja] Cola del hilo llevada al modelo visible: {movido * 1000f:F1} mm.");
        }

        /// <summary>
        /// Segundo intento de material, ya con todos los Awake ejecutados.
        ///
        /// MaterialSet publica su instancia en Awake, y Unity NO garantiza el
        /// orden de Awake entre objetos distintos: si la aguja despertaba
        /// primero, MaterialSet.I era null, el renderer se quedaba sin material
        /// y la aguja salía MAGENTA en la escena. Start siempre corre después
        /// de todos los Awake, así que aquí ya está disponible.
        /// </summary>
        void Start()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial == null) AssignMaterial(mr);
        }

        /// <summary>Un ForceGrab sin encaje: la próxima toma conserva la pose.</summary>
        [System.NonSerialized] public bool SkipNextSnap;

        void AlAgarrar(RoboticArm brazo)
        {
            if (SkipNextSnap) { SkipNextSnap = false; if (_encaje != null) StopCoroutine(_encaje); return; }
            if (!snapToGrip || brazo == null || !isActiveAndEnabled) return;
            if (_encaje != null) StopCoroutine(_encaje);
            _encaje = StartCoroutine(Encajar(brazo));
        }

        /// <summary>
        /// Lleva la aguja a la pose de agarre de un porta-agujas.
        ///
        /// Antes Graspable la emparentaba conservando su pose, así que quedaba
        /// como hubiera caído: de lado, con el arco fuera de las mandíbulas y
        /// el hilo saliendo de un punto que no cuadraba con la herramienta.
        /// </summary>
        System.Collections.IEnumerator Encajar(RoboticArm brazo)
        {
            Transform ancla = brazo.jawAnchor != null ? brazo.jawAnchor : brazo.wrist;
            if (ancla == null || transform.parent != ancla) yield break;

            // geometría del arco procedural (espacio local de la aguja: plano XY)
            float a0 = -arcDegrees * 0.5f * Mathf.Deg2Rad, a1 = arcDegrees * 0.5f * Mathf.Deg2Rad;
            float th = Mathf.Lerp(a0, a1, gripFromTail);
            Vector3 pL = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0f) * arcRadius;   // punto de agarre
            Vector3 tL = new Vector3(-Mathf.Sin(th), Mathf.Cos(th), 0f);              // alambre (cola→punta)
            Vector3 nL = Vector3.forward;                                              // normal del plano
            Quaternion baseL = Quaternion.LookRotation(nL, Vector3.Cross(nL, tL));
            Vector3 puntaL = Tip != null ? Tip.localPosition : pL;

            // ejes del instrumento en mundo: eje del vástago y eje de la bisagra
            Vector3 s = brazo.trocar != null && brazo.wrist != null
                ? (brazo.wrist.position - brazo.trocar.position).normalized : ancla.forward;
            Vector3 h = brazo.jawA != null && brazo.jawA.parent != null ? brazo.jawA.parent.right : ancla.right;
            h -= s * Vector3.Dot(h, s);
            if (h.sqrMagnitude < 1e-6f) h = Vector3.Cross(s, Vector3.up);
            h.Normalize();

            // de las cuatro combinaciones de signos, la que deja la punta más baja
            // (curva hacia el tejido, como se presenta al clavar)
            Vector3 escala = transform.lossyScale;
            Quaternion mejorR = transform.rotation;
            Vector3 mejorP = transform.position;
            float mejorY = float.MaxValue;
            foreach (float st in new[] { 1f, -1f })
            foreach (float sn in new[] { 1f, -1f })
            {
                Vector3 tW = h * st, nW = s * sn;
                Quaternion r = Quaternion.LookRotation(nW, Vector3.Cross(nW, tW)) * Quaternion.Inverse(baseL);
                Vector3 p = ancla.position - r * Vector3.Scale(pL, escala);
                float yPunta = (p + r * Vector3.Scale(puntaL, escala)).y;
                if (yPunta < mejorY) { mejorY = yPunta; mejorR = r; mejorP = p; }
            }

            // animar en el espacio del ancla para que siga a la herramienta
            Quaternion lr1 = Quaternion.Inverse(ancla.rotation) * mejorR;
            Vector3 lp1 = ancla.InverseTransformPoint(mejorP);
            Quaternion lr0 = transform.localRotation;
            Vector3 lp0 = transform.localPosition;
            for (float t = 0f; t < gripSeconds; t += Time.deltaTime)
            {
                if (transform.parent != ancla) yield break;   // la soltaron a mitad
                float k = Mathf.SmoothStep(0f, 1f, t / gripSeconds);
                transform.localPosition = Vector3.Lerp(lp0, lp1, k);
                transform.localRotation = Quaternion.Slerp(lr0, lr1, k);
                yield return null;
            }
            if (transform.parent == ancla)
            {
                transform.localPosition = lp1;
                transform.localRotation = lr1;
            }
            _encaje = null;
        }

        void AssignMaterial(MeshRenderer mr)
        {
            if (material != null) mr.sharedMaterial = material;
            else if (MaterialSet.I != null) mr.sharedMaterial = MaterialSet.I.needle;
        }
    }
}
