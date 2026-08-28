using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AdvancedRope;
using UnityEditor;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Construye el rig de sutura portado de SuturingSimulator: el colgajo de
    /// hígado deformable, sus SuturePoint sobre la incisión y el hilo Verlet.
    ///
    /// Todos los valores numéricos salen de LapSutureExtractor, que los leyó de
    /// Suturing_Rope.unity. Ninguno está inventado.
    ///
    /// El control NO entra por HapticPlugin sino por <see cref="SutureRig"/>,
    /// que lee de RoboticArm/MasterInput y por tanto conserva la conmutación
    /// Quest ↔ Touch.
    /// </summary>
    public static class SutureRigBuilder
    {
        const string ArtDir = "Assets/SutureTrainer/Suturing/Art";

        // --- valores extraídos de la escena original -------------------------
        const float DeformMass = 1f;
        const float DeformStiffnessOrigin = 50f;
        const float DeformStiffnessNeighbor = 0f;
        const float DeformDamping = 100f;
        const float DeformWeldTolerance = 0.0001f;
        const float DeformMaxVertexForce = 10f;
        const float DeformMaxVelocity = 0.01f;

        const float SutureStiffness = 100f;
        const float SutureThightness = 0.005f;
        const int SutureNeighborCount = 3;
        const int SutureTightnessVertexAmount = 555;

        const float RopeResolution = 0.5f;

        /// <summary>
        /// Radio del hilo, en metros del campo magnificado.
        ///
        /// El calibre anatómico se probó y NO sirve. Partiendo de la sutura
        /// real magnificada 5x (4-0 = 0.20 mm -> radio 0.500 mm) sale un tubo
        /// de 1 mm de diámetro que a la distancia de trabajo del visor ocupa
        /// uno o dos píxeles: en la grabación del 28-08 el hilo estaba ahí y
        /// simulando (51 nodos, tensión 0.99) y aun así era invisible.
        ///
        /// Así que se vuelve al valor del original escalado
        /// (0.00125 * FieldScale = 1.98 mm de radio, ~4 mm de diámetro). Es un
        /// cabo gordo para lo que representa, pero un entrenador tiene que
        /// dejar VER lo que hace el hilo; el calibre exacto no se está
        /// evaluando. Bajar de ~1.5 mm de radio lo hace desaparecer otra vez.
        /// </summary>
        const float RopeRadius = 0.00198f;

        /// <summary>
        /// Largo del cabo, en metros del campo.
        ///
        /// Los 0.10 m de antes dan para uno o dos puntos y se acaban: con la
        /// herida entera por coser hace falta cabo. El hilo propio del
        /// proyecto (VerletThread.totalLength) usa 1.1 m; aquí 0.30 m cubre la
        /// incisión con margen sin llenar el campo de hilo suelto.
        /// </summary>
        const float RopeLength = 0.30f;
        const float RopeStiffness = 0.2f;
        const int RopeIterations = 32;
        const float RopeContactFriction = 1f;
        const float RopeAirFriction = 1f;
        const float RopePushPower = 10f;
        const float RopeBodyForce = 1500f;
        const int RopeRadialSegments = 8;

        const bool PinEnd = true;
        const int PinIndex = 1;
        const float PinSlideThreshold = 0.03f;

        // Extremos del hilo en la escena original, en espacio local del hígado.
        // VerletRopeGenerator los interpreta en MUNDO, y si la distancia entre
        // ambos baja de 0.01 genera un cabo de 5 metros por defecto, así que
        // hay que fijarlos explícitamente.
        static readonly Vector3 RopeStartLocal = new Vector3(0.2117f, 0.0989f, 0.2765f);
        static readonly Vector3 RopeEndLocal = new Vector3(0.2095f, 0f, 0.274f);

        /// <summary>
        /// El colgajo mide 0.347 u de ancho; el campo quirúrgico de este
        /// proyecto usa TissuePatch.width = 0.55 m. Todo parámetro espacial
        /// heredado se multiplica por este factor.
        /// </summary>
        public const float FieldScale = 0.55f / 0.3467f;

        /// <summary>
        /// La incisión vive en el plano XY del modelo, o sea su superficie mira
        /// a +Z. Girar -90° sobre X la deja mirando hacia arriba (+Y), que es
        /// como se orienta el campo quirúrgico aquí.
        /// </summary>
        public static readonly Vector3 SurfaceEuler = new Vector3(-90f, 0f, 0f);

        /// <summary>Radio visible del marcador de punto de sutura, en metros.</summary>
        const float MarkerRadius = 0.003f;

        /// <summary>
        /// Radio del trigger, mayor que el visible para que la punta del
        /// instrumento lo capture sin exigir una precisión imposible.
        /// </summary>
        const float MarkerTriggerRadius = 0.008f;

        /// <summary>
        /// Radio del proxy que sigue la punta del instrumento. Sumado al trigger
        /// del marcador da el margen real de captura (~1.3 cm).
        /// </summary>
        const float ProxyRadius = 0.005f;

        /// <summary>
        /// Dónde nace la aguja respecto al centro del campo: a la derecha de la
        /// incisión y un poco por encima, al alcance de la pinza derecha.
        /// </summary>
        static readonly Vector3 NeedleSpawnOffset = new Vector3(0.20f, 0.08f, 0f);

        // ---------------------------------------------------------------------

        /// <summary>
        /// Monta el rig completo bajo <paramref name="fieldRoot"/> y lo conecta
        /// al brazo indicado. Devuelve la raíz creada, o null si falta el arte.
        /// </summary>
        /// <param name="standalone">
        /// true  = banco de pruebas (ST_06): monta TODO, incluidos hilo, aguja
        ///         y captura libre al cerrar el gatillo.
        /// false = dentro de un nivel: monta SOLO el órgano y sus puntos. El
        ///         nivel ya trae su aguja, su hilo y su propia lógica de fases;
        ///         duplicarlos deja dos agujas, dos hilos y dos sistemas de
        ///         captura compitiendo, que es justo lo que hacía que las cosas
        ///         se juntaran "solas" y que el hilo visible no se moviera.
        /// </param>
        public static GameObject Build(Transform fieldRoot, RoboticArm arm, bool standalone = true)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{ArtDir}/SS_Liver_Deformable.asset");
            if (mesh == null)
            {
                Debug.LogWarning("[SutureRig] Falta SS_Liver_Deformable.asset; no se monta el rig.");
                return null;
            }

            if (mesh.colors == null || mesh.colors.Length == 0)
            {
                Debug.LogError("[SutureRig] La malla no trae colores de vértice. " +
                               "DeformableMesh los usa para decidir qué vértices quedan anclados " +
                               "(isFixed = color.rgb < 0.5): sin ellos el órgano colapsa al simular.");
                return null;
            }

            var matStatic = LoadMat("mat_higado_static");
            var matDynamic = LoadMat("mat_higado_dynamic");
            var matSuture = LoadMat("mat_higado_suture");
            var matHilo = LoadMat("mat_hilo");

            // IMPORTANTE: el sistema portado asume que el transform del órgano
            // es la IDENTIDAD. SuturePoint hace
            //     transform.position = mesh.vertices[i] + offset
            // y DeformableMesh asigna InverseTransformPoint(...) a una posición
            // de mundo: mezclan espacio local y de mundo. En Suturing_Rope.unity
            // el hígado estaba en pos 0 / rot 0 / escala 1, así que colaba.
            //
            // Si aquí se coloca con rotación y escala, los SuturePoint acaban en
            // las coordenadas crudas de la malla (~0.2, 0.09, 0.25 en MUNDO), es
            // decir tirados junto a la mesa en vez de sobre el órgano.
            //
            // Solución: hornear la colocación en los propios vértices y dejar el
            // GameObject en la identidad. Así malla-local == mundo, igual que en
            // el original, y el código portado sigue sin tocarse.
            var rot = Quaternion.Euler(SurfaceEuler);
            Vector3 fieldPos = fieldRoot != null ? fieldRoot.position : Vector3.zero;
            Vector3 origin = fieldPos - rot * (mesh.bounds.center * FieldScale);
            var place = Matrix4x4.TRS(origin, rot, Vector3.one * FieldScale);

            var placed = Object.Instantiate(mesh);
            placed.name = "SS_Liver_Deformable_Placed";
            var verts = placed.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i] = place.MultiplyPoint3x4(verts[i]);
            placed.vertices = verts;

            var norms = placed.normals;
            if (norms != null && norms.Length == verts.Length)
            {
                for (int i = 0; i < norms.Length; i++)
                    norms[i] = place.MultiplyVector(norms[i]).normalized;
                placed.normals = norms;
            }
            placed.RecalculateBounds();
            AssetDatabase.CreateAsset(placed, $"{ArtDir}/SS_Liver_Deformable_Placed.asset");

            // La raíz NO cuelga del campo quirúrgico: debe quedar en la
            // identidad del mundo para que se cumpla lo anterior.
            var root = new GameObject("SuturingRig");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var liver = new GameObject("Higado_Deformable");
            liver.transform.SetParent(root.transform, false);
            liver.transform.localPosition = Vector3.zero;
            liver.transform.localRotation = Quaternion.identity;
            liver.transform.localScale = Vector3.one;

            liver.AddComponent<MeshFilter>().sharedMesh = placed;
            var lr = liver.AddComponent<MeshRenderer>();
            lr.sharedMaterials = new[] { matStatic, matDynamic, matSuture };

            var deform = liver.AddComponent<DeformableMesh>();
            deform.meshRoot = liver;
            deform.meshFilter = liver.GetComponent<MeshFilter>();
            deform.mass = DeformMass;
            deform.stiffnessOrigin = DeformStiffnessOrigin;
            deform.stiffnessNeighbor = DeformStiffnessNeighbor;
            deform.damping = DeformDamping;
            deform.weldTolerance = DeformWeldTolerance;
            deform.maxVertexForce = DeformMaxVertexForce;
            deform.maxVelocity = DeformMaxVelocity;
            deform.staticMaterial = matStatic;
            deform.dynamicMaterial = matDynamic;
            deform.sutureMaterial = matSuture;

            // ---------------- hilo (solo banco de pruebas) ----------------
            VerletRopeGenerator rope = null;
            BoyRopePinHole pinHole = null;
            if (standalone)
            {
            var hilo = new GameObject("HILO");
            hilo.transform.SetParent(root.transform, false);
            hilo.AddComponent<MeshFilter>();
            hilo.AddComponent<MeshRenderer>().sharedMaterial = matHilo;

            rope = hilo.AddComponent<VerletRopeGenerator>();
            rope.initializeOnStart = true;
            // en MUNDO: se llevan los extremos originales al sitio donde ha
            // quedado el colgajo dentro de este campo quirúrgico
            // La malla ya está horneada en mundo, así que los extremos del hilo
            // se llevan por la misma matriz de colocación.
            Vector3 ropeStart = place.MultiplyPoint3x4(RopeStartLocal);
            Vector3 ropeDir = (place.MultiplyPoint3x4(RopeEndLocal) - ropeStart).normalized;
            if (ropeDir.sqrMagnitude < 0.5f) ropeDir = Vector3.down;   // fallback
            rope.ropeStartPoint = ropeStart;
            rope.ropeEndPoint = ropeStart + ropeDir * RopeLength;
            rope.ropeResolution = RopeResolution;
            rope.ropeRadius = RopeRadius;
            rope.gravity = new Vector3(0f, -9.81f, 0f);
            rope.airFriction = RopeAirFriction;
            rope.stiffness = RopeStiffness;
            rope.constraintIterations = RopeIterations;
            rope.collisionPushPower = RopePushPower;
            rope.contactFriction = RopeContactFriction;
            rope.rigidbodyInteractionForce = RopeBodyForce;
            rope.enableCollisions = true;
            rope.enableSelfCollision = true;
            rope.meshRadialSegments = RopeRadialSegments;
            rope.meshTextureTiling = 1f;
            rope.initialPinPoints = new List<VerletRopeGenerator.PinConfiguration>();

            pinHole = hilo.AddComponent<BoyRopePinHole>();
            pinHole.verletRopeGenerator = rope;
            pinHole.pinEnd = PinEnd;
            pinHole.pinIndex = PinIndex;
            pinHole.slideThreshold = PinSlideThreshold * FieldScale;

            }

            // ---------------- gestor de sutura ----------------
            var mgrGO = new GameObject("SutureManager");
            mgrGO.transform.SetParent(root.transform, false);
            var mgr = mgrGO.AddComponent<SutureManager>();
            mgr.deformedMesh = deform;
            mgr.boyRopePinHole = pinHole;
            mgr.stiffnessSuture = SutureStiffness;
            mgr.thightness = SutureThightness * FieldScale;
            mgr.neighborCount = SutureNeighborCount;
            mgr.sutureTightnessVertexAmount = SutureTightnessVertexAmount;

            if (pinHole != null) pinHole.sutureManager = mgr;
            deform.sutueManager = mgr;

            // ---------------- puntos de sutura ----------------
            var pointsRoot = new GameObject("SuturingPoints");
            pointsRoot.transform.SetParent(liver.transform, false);
            var puntos = BuildSuturePoints(pointsRoot.transform, deform, LoadMat("mat_SuturePoint"), place,
                                            visible: standalone);
            int n = puntos.Count;

            // ---------------- puente con los niveles ----------------
            var organ = root.AddComponent<OrganRig>();
            organ.sutureManager = mgr;
            organ.ropePinHole = pinHole;
            organ.deformable = deform;
            BuildPairsFromModel(organ, puntos);

            // ---------------- superficie háptica ----------------
            // InstrumentHaptics calcula la fuerza de tejido contra el plano de
            // un TissuePatch. En los niveles ya existe uno (oculto y alineado
            // con la herida); aquí no había ninguno, y por eso el log mostraba
            // tejido=False y |fTejido|=0.00 N: solo se sentía el trócar.
            //
            // Se crea el mismo parche, oculto y alineado, en vez de duplicar la
            // matemática de la fuerza en otro sitio.
            if (standalone)
            {
                var hapticGO = new GameObject("TissuePatch (superficie háptica)");
                hapticGO.transform.SetParent(root.transform, false);

                Vector3 centro = Vector3.zero;
                foreach (var par in organ.pairs)
                    centro += (par.entry.transform.position + par.exit.transform.position) * 0.5f;
                if (organ.PairCount > 0) centro /= organ.PairCount;

                Vector3 alo = organ.PairCount > 1
                    ? (organ.pairs[organ.PairCount - 1].entry.transform.position -
                       organ.pairs[0].entry.transform.position).normalized
                    : Vector3.right;

                hapticGO.transform.SetPositionAndRotation(
                    centro, Quaternion.LookRotation(Vector3.Cross(alo, Vector3.up), Vector3.up));

                var tp = hapticGO.AddComponent<TissuePatch>();
                tp.hideVisual = true;
            }

            // ---------------- aguja ----------------
            // En SuturingSimulator lo que perfora los puntos es el "Proxy
            // Aguja", no la pinza: la pinza solo lo sujeta. Sin aguja en la
            // escena la mecánica no es la del original.
            //
            // Se usa el SutureNeedle de este proyecto, que ya trae Graspable y
            // por tanto lo recoge RoboticArm.UpdateGrasp sin código nuevo. Su
            // aspecto sale de ArtSet (modelo importado alineado sobre el arco
            // procedural, que es el que define Tip/Tail).
            SutureNeedle needle = null;
            if (standalone)
            {
            var needleGO = new GameObject("SutureNeedle");
            needleGO.transform.SetParent(root.transform, false);
            needleGO.transform.position = fieldPos + NeedleSpawnOffset;
            needle = needleGO.AddComponent<SutureNeedle>();

            // Que no se caiga al suelo al soltarla: en un banco de sutura
            // molesta más de lo que aporta.
            var grasp = needleGO.GetComponent<Graspable>();
            if (grasp != null) grasp.useGravityOnRelease = false;
            }

            // ---------------- control (Quest o Touch) ----------------
            if (standalone && arm != null)
            {
                var proxy = new GameObject("SutureProxy");
                proxy.transform.SetParent(root.transform, false);

                // Collider SÓLIDO (no trigger) + Rigidbody cinemático, igual que
                // el "Proxy Aguja" original: los SuturePoint son los triggers, y
                // Unity necesita el Rigidbody en uno de los dos para emitir
                // eventos. Con dos triggers y ningún Rigidbody no pasa nada.
                var sphere = proxy.AddComponent<SphereCollider>();
                sphere.isTrigger = false;
                sphere.radius = ProxyRadius;

                var rb = proxy.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                var rig = proxy.AddComponent<SutureRig>();
                rig.arm = arm;
                rig.sutureManager = mgr;
                rig.needle = needle;
                rig.ropePinHole = pinHole;

                // Renderer de las mandíbulas visibles: la captura mide contra
                // SU superficie, no contra un ancla deducida por geometría.
                foreach (var smr in arm.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0) continue;
                    rig.toolRenderer = smr;
                    break;
                }

                mgr.proxy = proxy;
                deform.proxy = proxy;
            }

            // En un nivel no hay hilo propio del rig: lo aporta el nivel.
            string hiloInfo = "sin hilo propio (lo pone el nivel)";
            if (rope != null)
            {
                float ropeLen = Vector3.Distance(rope.ropeStartPoint, rope.ropeEndPoint);
                hiloInfo = $"hilo radio {rope.ropeRadius * 1000f:F2} mm, largo {ropeLen:F3} m";
                if (ropeLen < 0.01f)
                    Debug.LogWarning("[SutureRig] Hilo demasiado corto: el generador lo sustituiría " +
                                     "por un cabo de 5 m. Revisa RopeStartLocal/RopeEndLocal.");
            }

            Debug.Log($"[SutureRig] Montado ({(standalone ? "banco de pruebas" : "dentro de nivel")}): " +
                      $"{mesh.vertexCount} vértices, {n} puntos en {organ.PairCount} pares, " +
                      $"escala {FieldScale:F3}, {hiloInfo}.");
            return root;
        }

        /// <summary>
        /// Crea los SuturePoint leyendo el volcado que hizo LapSutureExtractor
        /// de la escena original. Se omiten los que quedaron sin vértice
        /// asignado (vertexIndex = 0), que allí tampoco se usaban.
        /// </summary>
        /// <summary>
        /// Un punto creado, junto con sus coordenadas en el espacio DEL MODELO.
        /// Se conservan porque el emparejamiento debe hacerse ahí: en el modelo
        /// los labios de la herida se separan en Y y la incisión corre en X,
        /// semántica que el autor definió. En mundo esos ejes ya no significan
        /// eso (la colocación gira -90° en X, así que el eje de los labios pasa
        /// a ser Z), y separar por Y de mundo agrupaba puntos al azar.
        /// </summary>
        struct PuntoModelo
        {
            public SuturePoint punto;
            public float x;   // a lo largo de la incisión
            public float y;   // labio alto / labio bajo
        }

        static List<PuntoModelo> BuildSuturePoints(Transform parent, DeformableMesh deform,
                                     Material markerMaterial, Matrix4x4 place, bool visible)
        {
            string tsv = $"{ArtDir}/SuturePoints.tsv";
            var creados = new List<PuntoModelo>();
            if (!File.Exists(tsv))
            {
                Debug.LogWarning($"[SutureRig] No se encontró {tsv}; el rig queda sin puntos de sutura.");
                return creados;
            }

            var inv = CultureInfo.InvariantCulture;

            foreach (var line in File.ReadAllLines(tsv))
            {
                var f = line.Split('\t');
                if (f.Length < 8 || f[0] == "name") continue;
                if (!int.TryParse(f[1], NumberStyles.Integer, inv, out int vertexIndex)) continue;
                if (vertexIndex == 0) continue;   // sin asignar en el original

                // Los SuturePoint del original llevan SphereCollider (trigger)
                // + MeshRenderer: sin ellos son invisibles Y no pueden disparar
                // OnTriggerEnter, así que no se ven ni se puede suturar.
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = f[0];
                go.transform.SetParent(parent, false);
                go.transform.localScale = Vector3.one * (MarkerRadius * 2f);
                // El TSV guarda coordenadas locales del hígado ORIGINAL; se
                // llevan por la misma matriz con la que se horneó la malla, para
                // que en modo edición ya se vean sobre el órgano y no junto a la
                // mesa (en runtime SuturePoint.Update las recalcula igual).
                var tsvLocal = new Vector3(
                    float.Parse(f[5], inv), float.Parse(f[6], inv), float.Parse(f[7], inv));
                go.transform.localPosition = place.MultiplyPoint3x4(tsvLocal);

                var col = go.GetComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.5f * (MarkerTriggerRadius / MarkerRadius);

                var mr = go.GetComponent<MeshRenderer>();
                if (markerMaterial != null) mr.sharedMaterial = markerMaterial;

                // Dentro de un nivel estos marcadores son solo anclas: quien
                // guía es el PunctureTarget del nivel, que sí muestra estado
                // (inactivo / activo / hecho). Verlos a la vez duplicaba las
                // esferas sobre la incisión y confundía qué había que hacer.
                mr.enabled = visible;

                var sp = go.AddComponent<SuturePoint>();
                sp.deformedMesh = deform;
                sp.vertexIndex = vertexIndex;
                // el offset se suma a un vértice ya horneado, así que hay que
                // rotarlo y escalarlo igual
                var rawOffset = new Vector3(
                    float.Parse(f[2], inv), float.Parse(f[3], inv), float.Parse(f[4], inv));
                sp.offset = place.MultiplyVector(rawOffset);
                creados.Add(new PuntoModelo
                {
                    punto = sp,
                    x = float.Parse(f[5], inv),
                    y = float.Parse(f[6], inv)
                });
            }

            return creados;
        }

        /// <summary>
        /// Empareja labio alto con labio bajo usando las coordenadas del MODELO,
        /// y en orden a lo largo de la incisión. Descarta duplicados (el
        /// original trae SuturePoint_0b sobre SuturePoint_0, y sin quitarlo el
        /// emparejamiento se desplaza una posición y las puntadas salen en
        /// diagonal).
        /// </summary>
        static void BuildPairsFromModel(OrganRig organ, List<PuntoModelo> puntos)
        {
            organ.pairs.Clear();
            if (puntos.Count == 0) return;

            const float coincidentes = 0.002f;
            var unicos = new List<PuntoModelo>();
            foreach (var p in puntos)
            {
                bool repe = false;
                foreach (var q in unicos)
                    if (Mathf.Abs(p.x - q.x) < coincidentes && Mathf.Abs(p.y - q.y) < coincidentes)
                    { repe = true; break; }
                if (!repe) unicos.Add(p);
            }

            float mediaY = 0f;
            foreach (var p in unicos) mediaY += p.y;
            mediaY /= unicos.Count;

            var altos = new List<PuntoModelo>();
            var bajos = new List<PuntoModelo>();
            foreach (var p in unicos) (p.y >= mediaY ? altos : bajos).Add(p);
            altos.Sort((a, b) => a.x.CompareTo(b.x));

            foreach (var alto in altos)
            {
                if (bajos.Count == 0) break;

                int mejor = 0;
                float mejorD = float.MaxValue;
                for (int i = 0; i < bajos.Count; i++)
                {
                    float d = Mathf.Abs(alto.x - bajos[i].x);
                    if (d < mejorD) { mejorD = d; mejor = i; }
                }

                organ.pairs.Add(new OrganRig.StitchPair
                {
                    entry = alto.punto,
                    exit = bajos[mejor].punto
                });
                bajos.RemoveAt(mejor);
            }
        }

        /// <summary>
        /// Carga un material del arte portado.
        ///
        /// Busca fuera de ArtDir si hace falta porque el arte llegó repartido:
        /// mat_higado_static acabó en Art/Materials/ y el resto en
        /// Suturing/Art/. Con la ruta fija, matStatic salía null y el renderer
        /// del hígado se quedaba con un hueco en sharedMaterials, que Unity
        /// pinta de MAGENTA. En pantalla parecía un shader roto, pero el
        /// shader estaba bien: no había material.
        ///
        /// Un material que no aparece es un error de verdad, así que se avisa
        /// en vez de devolver null en silencio y dejar que se vea en el visor.
        /// </summary>
        static Material LoadMat(string name)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{ArtDir}/{name}.mat");
            if (mat != null) return mat;

            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;

                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                {
                    Debug.LogWarning($"[SutureRig] {name}.mat no está en {ArtDir} " +
                                     $"sino en {path}. Se usa igualmente.");
                    return mat;
                }
            }

            Debug.LogError($"[SutureRig] Falta {name}.mat: lo que lo use saldrá " +
                           "de color magenta en la escena.");
            return null;
        }
    }
}
