using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Prefabs de arte importados de SuturingSimulator. Todos son opcionales:
    /// si uno falta, el objeto correspondiente cae a su geometría procedural de
    /// siempre, así que la escena nunca queda rota.
    ///
    /// REGLA IMPORTANTE: el arte solo cambia lo VISUAL. Los colisionadores, los
    /// transforms Tip/Tail de la aguja y las medidas de los ejercicios siguen
    /// siendo los procedurales. Por eso el arte tiene que quedar ENCIMA de la
    /// geometría procedural: si no coinciden, el usuario apunta con lo que ve y
    /// el código mide otro punto, y los ejercicios se vuelven imposibles.
    /// Usa <see cref="showProceduralGuides"/> para verificar que encajan.
    /// </summary>
    public class ArtSet : MonoBehaviour
    {
        public static ArtSet I { get; private set; }

        [Header("Diagnóstico")]
        [Tooltip("Muestra también la geometría procedural (la que mide la lógica) " +
                 "junto al arte. Si no se superponen, el arte está desalineado y " +
                 "los ejercicios fallarán.")]
        public bool showProceduralGuides;

        [Header("Instrumentos (SkinnedMeshRenderer con blendshape 'Closed')")]
        public GameObject instrumentLeft;
        public GameObject instrumentRight;
        public float instrumentScale = 1f;
        public Vector3 instrumentEuler = Vector3.zero;
        [Tooltip("Corrige que el origen del modelo no esté sobre el eje del vástago.")]
        public Vector3 instrumentOffset = Vector3.zero;

        [Header("Aguja")]
        public GameObject needle;
        public float needleScale = 1f;
        public Vector3 needleEuler = Vector3.zero;
        [Tooltip("Ajuste fino sobre el alineado automático con el arco procedural.")]
        public Vector3 needleOffset = Vector3.zero;

        [Header("Tejido")]
        public GameObject tissue;
        public float tissueScale = 1f;
        public Vector3 tissueEuler = Vector3.zero;
        public Vector3 tissueOffset = new Vector3(0f, -0.02f, 0f);
        [Tooltip("Oculta el parche plano procedural y deja solo el órgano. " +
                 "Nota: el órgano NO se deforma.")]
        public bool tissueReplacesPatch = true;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------

        /// <summary>
        /// Cuelga un prefab de arte como puro visual: sin colisionadores y sin
        /// participación en física.
        /// </summary>
        public static Transform AttachVisual(GameObject prefab, Transform parent,
                                             float scale, Vector3 euler,
                                             Vector3 localOffset = default)
        {
            if (prefab == null || parent == null) return null;

            var go = Instantiate(prefab, parent);
            go.name = prefab.name + "_Visual";
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = Vector3.one * scale;

            // Se llama desde el SceneBuilder (modo edición) y desde Awake en
            // runtime; DestroyImmediate no es válido durante el juego.
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            return go.transform;
        }

        /// <summary>
        /// Bounds combinados de todas las mallas del prefab, en el espacio
        /// local de su raíz.
        /// </summary>
        public static bool TryGetLocalBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            if (prefab == null) return false;

            bool any = false;
            var root = prefab.transform;
            // acumulador local: C# no permite escribir un parámetro 'out'
            // desde dentro de una función local
            Bounds acc = default;

            void Add(Mesh mesh, Transform t)
            {
                if (mesh == null) return;
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f,
                        (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = root.InverseTransformPoint(t.TransformPoint(corner));
                    if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                    else acc.Encapsulate(p);
                }
            }

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                Add(mf.sharedMesh, mf.transform);
            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Add(smr.sharedMesh, smr.transform);

            bounds = acc;
            return any;
        }

        /// <summary>
        /// Escala que hace que la dimensión mayor del prefab mida
        /// <paramref name="targetSize"/> metros. Los modelos vienen en unidades
        /// de Blender/glTF sin relación con la escena (la pinza mide ~9
        /// unidades, la aguja ~0.77), así que fijar el factor a mano es adivinar.
        /// </summary>
        public static float ComputeFitScale(GameObject prefab, float targetSize)
        {
            if (targetSize <= 0f || !TryGetLocalBounds(prefab, out var b)) return 1f;
            float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            return max > 1e-6f ? targetSize / max : 1f;
        }

        /// <summary>
        /// Desplazamiento que sitúa el centro del prefab (ya escalado y rotado)
        /// sobre <paramref name="targetLocalCenter"/>. Con esto el arte queda
        /// encima de la geometría procedural que mide la lógica.
        /// </summary>
        public static Vector3 ComputeAlignOffset(GameObject prefab, float scale,
                                                 Vector3 euler, Vector3 targetLocalCenter)
        {
            if (!TryGetLocalBounds(prefab, out var b)) return Vector3.zero;
            Vector3 rotated = Quaternion.Euler(euler) * (b.center * scale);
            return targetLocalCenter - rotated;
        }

        /// <summary>
        /// Desplazamiento que apoya la CARA SUPERIOR del prefab sobre el origen
        /// del padre, centrándolo lateralmente.
        ///
        /// Para el órgano esto es imprescindible: centrarlo por su volumen lo
        /// deja sobresaliendo ~13 cm por encima del tejido y sepulta la línea de
        /// herida (y = 0.013) y los marcadores de punción (y ≈ 0), que son
        /// justo lo que hay que suturar.
        /// </summary>
        /// <param name="alignTop">
        /// true: apoya la cara SUPERIOR sobre el plano (el órgano queda hundido,
        /// pero la herida y los marcadores procedurales siguen visibles).
        /// false: apoya la cara INFERIOR, o sea el órgano se posa SOBRE el campo
        /// y se ve entero, como en SuturingSimulator. En ese caso la incisión
        /// que se sutura es la del propio modelo, no los marcadores.
        /// </param>
        public static Vector3 ComputeSurfaceOffset(GameObject prefab, float scale,
                                                   Vector3 euler, bool alignTop,
                                                   float sink = 0.002f)
        {
            if (!TryGetLocalBounds(prefab, out var b)) return Vector3.zero;

            var rot = Quaternion.Euler(euler);
            bool first = true;
            Bounds r = default;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
                Vector3 p = rot * (corner * scale);
                if (first) { r = new Bounds(p, Vector3.zero); first = false; }
                else r.Encapsulate(p);
            }

            float y = alignTop ? -r.max.y - sink : -r.min.y + sink;
            return new Vector3(-r.center.x, y, -r.center.z);
        }

        /// <summary>
        /// Igual que <see cref="ComputeAlignOffset"/> pero solo en los ejes
        /// perpendicular al vástago: centra el eje del instrumento sobre la
        /// muñeca sin moverlo a lo largo de su propia longitud.
        /// </summary>
        public static Vector3 ComputeShaftCenteringOffset(GameObject prefab, float scale,
                                                          Vector3 euler)
        {
            var off = ComputeAlignOffset(prefab, scale, euler, Vector3.zero);
            off.z = 0f;   // tras la rotación el vástago corre a lo largo de Z
            return off;
        }
    }
}
