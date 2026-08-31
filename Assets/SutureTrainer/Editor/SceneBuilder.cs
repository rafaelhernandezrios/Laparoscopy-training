using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using TMPro;
using System.Collections.Generic;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Construye automáticamente materiales y las 6 escenas del entrenador
    /// (menú + 5 niveles). Menú: SutureTrainer → Construir todo.
    /// </summary>
    public static class SceneBuilder
    {
        const string Root = "Assets/SutureTrainer";
        const string MatDir = Root + "/Materials";
        const string SceneDir = Root + "/Scenes";

        /// <summary>
        /// Centro del campo quirúrgico. Es la cota de referencia de todos los
        /// ejercicios, así que vive aquí y la usan tanto el campo como el
        /// quirófano que se coloca a su alrededor.
        /// </summary>
        static readonly Vector3 FieldPos = new Vector3(0f, 1.08f, 0.55f);

        static Dictionary<string, Material> mats;
        static ArtSet art;   // arte de la escena en construcción (puede ser null)

        [MenuItem("SutureTrainer/Construir todo (materiales + escenas)")]
        public static void BuildAll()
        {
            EnsureFolders();
            CreateMaterials();

            BuildScene(GameFlow.Scenes[0], null);
            BuildScene(GameFlow.Scenes[1], typeof(Level1_NeedleHandling));
            BuildScene(GameFlow.Scenes[2], typeof(Level2_RingPass));
            BuildScene(GameFlow.Scenes[3], typeof(Level3_SimpleStitch));
            BuildScene(GameFlow.Scenes[4], typeof(Level4_RunningSuture));
            BuildScene(GameFlow.Scenes[5], typeof(Level5_KnotTying));

            var list = new List<EditorBuildSettingsScene>();
            foreach (var s in GameFlow.Scenes)
                list.Add(new EditorBuildSettingsScene($"{SceneDir}/{s}.unity", true));
            EditorBuildSettings.scenes = list.ToArray();

            // La escena de órgano se construye aquí dentro, no aparte. Cuando
            // vivía solo en su propio menú, cualquier "Construir todo" posterior
            // reescribía la lista de Build Settings sin ella y el botón 6 del
            // menú se quedaba apuntando a una escena que no estaba en la build.
            BuildSutureScene();

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene($"{SceneDir}/{GameFlow.Scenes[0]}.unity");
            Debug.Log("[SutureTrainer] Escenas construidas y añadidas a Build Settings. ¡Listo!");
        }

        /// <summary>
        /// Escena de réplica: el colgajo de hígado deformable de
        /// SuturingSimulator, sus puntos de sutura y el hilo Verlet, pero
        /// teleoperados desde el rig de este proyecto, así que funciona igual
        /// con mando de Quest o con el 3D Systems Touch (F1 conmuta).
        ///
        /// Se construye aparte de los 5 niveles para poder validar la fidelidad
        /// sin tocar el temario ya calibrado.
        /// </summary>
        [MenuItem("SutureTrainer/Construir escena de sutura con órgano real")]
        public static void BuildSutureScene()
        {
            EnsureFolders();
            CreateMaterials();

            const string sceneName = GameFlow.OrganScene;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.30f, 0.34f);

            BuildLights();
            float tableTopY = BuildEnvironment();
            var camOffset = BuildXRRig();
            var field = BuildSurgicalField(tableTopY);

            var mgr = new GameObject("GameManager");
            var matSet = mgr.AddComponent<MaterialSet>();
            WireMaterialSet(matSet);

            art = mgr.AddComponent<ArtSet>();
            WireArtSet(art);
            // aquí el tejido es el órgano deformable del rig, no un parche
            art.tissue = null;

            var masterR = mgr.AddComponent<MasterInput>();
            masterR.node = XRNode.RightHand;
            masterR.trackingSpace = camOffset;
            masterR.toggleKey = UnityEngine.InputSystem.Key.F1;

            var masterL = mgr.AddComponent<MasterInput>();
            masterL.node = XRNode.LeftHand;
            masterL.trackingSpace = camOffset;

            // Ancla del Touch SOBRE el campo quirúrgico.
            //
            // Sin esto, touchHome queda vacío y el Touch se ancla donde
            // estuviera el instrumento al pulsar F1: un punto arbitrario. El
            // log lo delataba con penetraciones de -166 mm a +364 mm y la
            // fuerza de trócar clavada en su máximo, porque la punta nunca
            // estaba cerca del tejido.
            var touchHome = new GameObject("TouchHome").transform;
            touchHome.SetParent(field, false);
            touchHome.localPosition = new Vector3(0f, 0.04f, 0f);
            masterR.touchHome = touchHome;

            // 2.5 amplificaba los ~16 cm del stylus a 40 cm de recorrido: para
            // acertar una diana de 14 mm había que afinar a 5.6 mm de mano.
            // La incisión mide ~80 mm, así que 1.2 (≈19 cm de recorrido) la
            // cubre de sobra y deja el movimiento casi 1:1.
            masterR.touchWorkspaceScale = 1.2f;

            var laser = mgr.AddComponent<ControllerLaser>();
            laser.master = masterR;
            // Salir al menú con la IZQUIERDA: B/Y de la derecha ya tensa la
            // sutura, y mantenerlo para apretar el nudo sacaba de la escena.
            laser.exitMaster = masterL;
            laser.startActive = false;

            var armL = BuildArm(false, masterL, field);
            var armR = BuildArm(true, masterR, field);

            SutureRigBuilder.Build(field, armR);

            // NO se añade el XR Device Simulator.
            //
            // Sirve para probar sin visor, pero crea dispositivos XR SIMULADOS
            // que compiten con los reales: con Quest Link y el Touch conectados
            // puede eclipsar el tracking auténtico. Como el uso previsto es
            // hardware real (OPENHAPTICS_TOUCH solo está definido en Standalone),
            // meterlo por defecto hace más daño que bien.
            //
            // Si alguna vez hace falta probar sin visor, arrástralo a mano desde
            // Assets/Samples/XR Interaction Toolkit/3.4.1/XR Device Simulator/
            // y quítalo antes de volver a probar en el visor.

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{sceneName}.unity");

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            string path = $"{SceneDir}/{sceneName}.unity";
            if (!scenes.Exists(s => s.path == path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            Debug.Log($"[SutureTrainer] Escena de sutura creada: {path}");
        }

        // ------------------------------------------------------------------
        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "SutureTrainer");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");
            if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder(Root, "Scenes");
        }

        static Material Mat(string name, Color c, float metallic = 0f, float smooth = 0.5f, Color? emission = null)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static void CreateMaterials()
        {
            mats = new Dictionary<string, Material>();
            Mat("Metal", new Color(0.75f, 0.77f, 0.80f), 0.9f, 0.75f);
            Mat("DarkMetal", new Color(0.18f, 0.19f, 0.22f), 0.6f, 0.4f);
            Mat("Needle", new Color(0.85f, 0.87f, 0.90f), 1f, 0.9f);
            Mat("Thread", new Color(0.15f, 0.25f, 0.75f), 0f, 0.4f);
            Mat("ThreadTense", new Color(0.9f, 0.15f, 0.1f), 0f, 0.4f, new Color(0.6f, 0.05f, 0.02f));
            Mat("Tissue", new Color(0.85f, 0.55f, 0.50f), 0f, 0.35f);
            Mat("Wound", new Color(0.45f, 0.08f, 0.08f), 0f, 0.3f);
            Mat("MarkerIdle", new Color(0.45f, 0.45f, 0.45f), 0f, 0.3f);
            Mat("MarkerEntry", new Color(0.2f, 0.9f, 0.3f), 0f, 0.4f, new Color(0.05f, 0.5f, 0.1f));
            Mat("MarkerExit", new Color(1f, 0.6f, 0.1f), 0f, 0.4f, new Color(0.6f, 0.3f, 0.02f));
            Mat("MarkerDone", new Color(0.25f, 0.45f, 0.9f), 0f, 0.4f);
            Mat("Ring", new Color(0.5f, 0.5f, 0.55f), 0.7f, 0.6f);
            Mat("RingActive", new Color(0.3f, 0.9f, 0.9f), 0.2f, 0.6f, new Color(0.05f, 0.5f, 0.5f));
            Mat("RingDone", new Color(0.25f, 0.7f, 0.3f), 0.2f, 0.5f);
            Mat("Ghost", new Color(0.3f, 1f, 0.6f), 0f, 0.2f, new Color(0.1f, 0.6f, 0.3f));
            Mat("Panel", new Color(0.08f, 0.09f, 0.12f), 0f, 0.2f);
            Mat("ButtonIdle", new Color(0.15f, 0.25f, 0.45f), 0f, 0.4f);
            Mat("ButtonHover", new Color(0.25f, 0.5f, 0.9f), 0f, 0.5f, new Color(0.1f, 0.25f, 0.5f));
            Mat("Laser", new Color(0.4f, 0.9f, 1f), 0f, 0.5f, new Color(0.2f, 0.6f, 0.8f));
            Mat("Floor", new Color(0.12f, 0.13f, 0.15f), 0f, 0.2f);
            Mat("StarOn", new Color(1f, 0.85f, 0.2f), 0f, 0.6f, new Color(0.7f, 0.55f, 0.05f));
            Mat("StarOff", new Color(0.25f, 0.25f, 0.28f), 0f, 0.2f);
        }

        // ------------------------------------------------------------------
        static void BuildScene(string sceneName, System.Type levelType)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.30f, 0.34f);

            BuildLights();
            float tableTopY = BuildEnvironment();
            var camOffset = BuildXRRig();
            var field = BuildSurgicalField(tableTopY);

            // gestor central
            var mgr = new GameObject("GameManager");
            var matSet = mgr.AddComponent<MaterialSet>();
            WireMaterialSet(matSet);

            // Arte importado de SuturingSimulator. Es opcional: si la carpeta
            // Art/ no está, cada prefab queda en null y todo cae a la geometría
            // procedural de siempre.
            art = mgr.AddComponent<ArtSet>();
            WireArtSet(art);

            var masterR = mgr.AddComponent<MasterInput>();
            masterR.node = XRNode.RightHand; masterR.trackingSpace = camOffset;
            // Mano derecha conmutable Quest <-> Touch. Por defecto Quest (para no
            // romper si el Touch no está); pulsa F1 en runtime para cambiar, o
            // fija backend = TouchHaptic en el inspector / SceneBuilder.
            masterR.toggleKey = UnityEngine.InputSystem.Key.F1;
            var masterL = mgr.AddComponent<MasterInput>();
            masterL.node = XRNode.LeftHand; masterL.trackingSpace = camOffset;

            // Ancla del Touch SOBRE el campo quirúrgico.
            //
            // Sin esto, touchHome queda vacío y el Touch se ancla donde
            // estuviera el instrumento al pulsar F1: un punto arbitrario. El
            // log lo delataba con penetraciones de -166 mm a +364 mm y la
            // fuerza de trócar clavada en su máximo, porque la punta nunca
            // estaba cerca del tejido.
            var touchHome = new GameObject("TouchHome").transform;
            touchHome.SetParent(field, false);
            touchHome.localPosition = new Vector3(0f, 0.04f, 0f);
            masterR.touchHome = touchHome;

            // 2.5 amplificaba los ~16 cm del stylus a 40 cm de recorrido: para
            // acertar una diana de 14 mm había que afinar a 5.6 mm de mano.
            // La incisión mide ~80 mm, así que 1.2 (≈19 cm de recorrido) la
            // cubre de sobra y deja el movimiento casi 1:1.
            masterR.touchWorkspaceScale = 1.2f;

            var laser = mgr.AddComponent<ControllerLaser>();
            laser.master = masterR;
            // Salir al menú con la IZQUIERDA: B/Y de la derecha ya tensa la
            // sutura, y mantenerlo para apretar el nudo sacaba de la escena.
            laser.exitMaster = masterL;
            laser.startActive = levelType == null;

            if (levelType == null)
            {
                BuildMenu();
            }
            else
            {
                var metrics = mgr.AddComponent<MetricsRecorder>();
                var armL = BuildArm(false, masterL, field);
                var armR = BuildArm(true, masterR, field);
                metrics.leftArm = armL; metrics.rightArm = armR;

                // Lazo adaptativo. El controlador arranca en d = 0.5, que
                // reproduce exactamente la configuración de serie, así que
                // estando presente no cambia nada por sí solo. La fuente de
                // carga queda desactivada: actívala (y elige UDP o Replay) para
                // cerrar el lazo.
                mgr.AddComponent<AdaptiveDifficultyController>();
                var load = mgr.AddComponent<LoadSource>();
                load.mode = LoadSource.Mode.Replay;
                load.enabled = false;

                var hud = BuildHUD();
                var results = BuildResultsPanel();

                // Niveles de sutura (3, 4 y 5): montar el órgano portado para
                // que sus dianas caigan sobre la incisión real. Los niveles
                // detectan el OrganRig solos; si no está, siguen con el parche.
                if (levelType == typeof(Level3_SimpleStitch) ||
                    levelType == typeof(Level4_RunningSuture) ||
                    levelType == typeof(Level5_KnotTying))
                {
                    // standalone:false -> sin hilo, sin aguja y sin captura
                    // libre: el nivel ya aporta los suyos.
                    SutureRigBuilder.Build(field, armR, standalone: false);
                }

                var level = (TrainingLevel)mgr.AddComponent(levelType);
                level.leftArm = armL; level.rightArm = armR;
                level.fieldRoot = field; level.hud = hud; level.results = results;
                ApplyThresholds(level, sceneName);
            }

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{sceneName}.unity");
        }

        static void ApplyThresholds(TrainingLevel level, string sceneName)
        {
            switch (System.Array.IndexOf(GameFlow.Scenes, sceneName))
            {
                case 1: level.parTimeSec = 90f; level.parPathMeters = 8f; level.maxErrors = 1; break;
                case 2: level.parTimeSec = 120f; level.parPathMeters = 10f; level.maxErrors = 1; break;
                case 3: level.parTimeSec = 210f; level.parPathMeters = 16f; level.maxErrors = 2; break;
                case 4: level.parTimeSec = 300f; level.parPathMeters = 22f; level.maxErrors = 2; break;
                case 5: level.parTimeSec = 360f; level.parPathMeters = 26f; level.maxErrors = 3; break;
            }
        }

        /// <summary>
        /// Enlaza los prefabs de arte horneados desde SuturingSimulator. Cada
        /// uno es opcional a propósito: el arte solo sustituye lo visual, nunca
        /// los colisionadores ni las medidas calibradas de los ejercicios.
        /// </summary>
        static void WireArtSet(ArtSet s)
        {
            const string ArtDir = Root + "/Art";

            GameObject Load(string name) =>
                AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtDir}/{name}.prefab");

            s.needle = Load("SS_Needle");
            s.instrumentLeft = Load("SS_Instrument_L");
            s.instrumentRight = Load("SS_Instrument_R");
            s.tissue = Load("SS_Liver");

            // Escalas deducidas de la malla, no fijadas a mano: los modelos
            // vienen en unidades de Blender/glTF (la pinza mide ~9 unidades) y
            // cualquier factor inventado los deja gigantes o invisibles.
            //
            // Tamaños objetivo, en metros de la escena magnificada:
            //  - instrumento 0.40 m  -> vástago del trócar a la muñeca + mandíbulas
            //  - aguja       0.11 m  -> diámetro del arco procedural (arcRadius 0.055 * 2)
            //  - tejido      0.55 m  -> TissuePatch.width
            // ?? no sirve con objetos de Unity: no respeta su operador == sobrecargado
            var fitRef = s.instrumentRight != null ? s.instrumentRight : s.instrumentLeft;
            s.instrumentScale = ArtSet.ComputeFitScale(fitRef, 0.40f);
            s.needleScale = ArtSet.ComputeFitScale(s.needle, 0.11f);
            s.tissueScale = ArtSet.ComputeFitScale(s.tissue, 0.55f);

            // Orientación de la pinza.
            // Los hijos del prefab ya traen la rotación -90° en X de conversión
            // Blender (Z-up) -> Unity (Y-up). Con ella, el eje largo del modelo
            // (-Y en espacio de malla) queda apuntando a +Z en espacio del
            // prefab: es decir, el vástago sale hacia DELANTE. Pero la muñeca
            // del brazo apunta las mandíbulas a +Z (jawAnchor en z = +0.06) y
            // espera el vástago hacia -Z, hacia el trócar.
            // Girar 180° sobre Y invierte el eje y deja el modelo del derecho.
            s.instrumentEuler = new Vector3(0f, 180f, 0f);

            // Orientación de la aguja.
            // El arco procedural (ProcMesh.ArcTube) vive en el plano XY, con
            // normal Z. El modelo importado es delgado en X, o sea su arco está
            // en el plano YZ con normal X. Girar 90° sobre Y lleva esa normal
            // a Z y hace coincidir ambos planos.
            s.needleEuler = new Vector3(0f, 90f, 0f);

            // El origen del modelo NO está sobre el eje del vástago (el Body
            // está centrado en x = -0.90), así que sin esto la pinza cuelga
            // descentrada de la muñeca y se siente torcida al rotar.
            s.instrumentOffset = ArtSet.ComputeShaftCenteringOffset(
                fitRef, s.instrumentScale, s.instrumentEuler);

            // El órgano se posa SOBRE el campo y se ve entero, como en
            // SuturingSimulator. Su material es mat_higado_static, que pinta
            // tex_higado_cortado: la incisión con su margen viene en la propia
            // textura del modelo, no de los marcadores procedurales.
            s.tissueOffset = ArtSet.ComputeSurfaceOffset(
                s.tissue, s.tissueScale, s.tissueEuler, alignTop: false);

            Debug.Log($"[SutureTrainer] Arte -> instrumento escala {s.instrumentScale:F4} " +
                      $"offset {s.instrumentOffset}, aguja {s.needleScale:F4}, " +
                      $"tejido {s.tissueScale:F4}");

            if (s.needle == null && s.instrumentLeft == null && s.instrumentRight == null)
                Debug.Log("[SutureTrainer] Sin carpeta Art/: se usa la geometría procedural.");
        }

        static void WireMaterialSet(MaterialSet s)
        {
            s.metal = mats["Metal"]; s.darkMetal = mats["DarkMetal"]; s.needle = mats["Needle"];
            s.thread = mats["Thread"]; s.threadTense = mats["ThreadTense"];
            s.tissue = mats["Tissue"]; s.wound = mats["Wound"];
            s.markerIdle = mats["MarkerIdle"]; s.markerEntry = mats["MarkerEntry"];
            s.markerExit = mats["MarkerExit"]; s.markerDone = mats["MarkerDone"];
            s.ring = mats["Ring"]; s.ringActive = mats["RingActive"]; s.ringDone = mats["RingDone"];
            s.ghost = mats["Ghost"]; s.panel = mats["Panel"];
            s.buttonIdle = mats["ButtonIdle"]; s.buttonHover = mats["ButtonHover"];
            s.laser = mats["Laser"]; s.floor = mats["Floor"];
        }

        // ------------------------------------------------------------------
        static void BuildLights()
        {
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.7f;
            sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            var spot = new GameObject("Surgical Spot").AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.transform.position = new Vector3(0f, 2.2f, 0.55f);
            spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            spot.range = 4f; spot.spotAngle = 70f; spot.intensity = 4f;
        }

        /// <summary>
        /// Entorno de la escena. Si el quirófano importado está disponible, es
        /// él quien pone suelo y mobiliario; el disco y la consola procedurales
        /// solo aparecen como respaldo, para que el proyecto siga arrancando
        /// sin el modelo.
        /// </summary>
        /// <returns>
        /// Altura del tablero de la mesa de operaciones, o NaN si se usó el
        /// entorno procedural.
        /// </returns>
        static float BuildEnvironment()
        {
            var env = new GameObject("Environment");

            var room = OperatingRoomBuilder.Build(env.transform, FieldPos);
            if (room.built) return room.tableTopY;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.transform.SetParent(env.transform);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(8f, 0.05f, 8f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mats["Floor"];

            // mesa/consola frente al usuario
            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            desk.name = "ConsoleDesk";
            desk.transform.SetParent(env.transform);
            desk.transform.position = new Vector3(0f, 0.5f, 0.55f);
            desk.transform.localScale = new Vector3(1.1f, 1.0f, 0.7f);
            desk.GetComponent<MeshRenderer>().sharedMaterial = mats["DarkMetal"];

            return float.NaN;
        }

        static Transform BuildXRRig()
        {
            var rig = new GameObject("XR Origin");
            var origin = rig.AddComponent<XROrigin>();

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(rig.transform, false);

            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(offset.transform, false);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Azul de hospital: se ve en techo y huecos que la geometría del
            // quirófano no cubre, así que un negro casi puro (como antes)
            // destacaba como un agujero. Este tono es el que asoma detrás de
            // la sala, no una pared pintada.
            cam.backgroundColor = new Color(0.55f, 0.66f, 0.74f);
            cam.nearClipPlane = 0.03f;
            camGO.AddComponent<AudioListener>();

            var tpd = camGO.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new UnityEngine.InputSystem.InputActionProperty(
                new UnityEngine.InputSystem.InputAction(binding: "<XRHMD>/centerEyePosition"));
            tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(
                new UnityEngine.InputSystem.InputAction(binding: "<XRHMD>/centerEyeRotation"));

            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            rig.AddComponent<RigRecenter>();

            return offset.transform;
        }

        /// <summary>
        /// Campo quirúrgico. La base es una bandeja de 6 cm sobre el vacío
        /// cuando el entorno es procedural; con el quirófano montado se estira
        /// hasta el tablero de la mesa (<paramref name="tableTopY"/>) para que
        /// no quede flotando por encima de él.
        /// </summary>
        static Transform BuildSurgicalField(float tableTopY)
        {
            var field = new GameObject("SurgicalField");
            field.transform.position = FieldPos;

            // Cara superior de la bandeja, 5 mm por debajo del plano de trabajo.
            const float topOffset = -0.005f;
            float height = 0.06f;

            if (!float.IsNaN(tableTopY))
            {
                float gap = FieldPos.y + topOffset - tableTopY;
                if (gap > height) height = gap;
            }

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "FieldBase";
            table.transform.SetParent(field.transform, false);
            table.transform.localPosition = new Vector3(0f, topOffset - height * 0.5f, 0f);
            table.transform.localScale = new Vector3(0.8f, height, 0.6f);
            table.GetComponent<MeshRenderer>().sharedMaterial = mats["Metal"];

            return field.transform;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// Deja el objeto sin nada que dibujar, conservando su Transform.
        /// Para primitivas procedurales que un modelo importado sustituye pero
        /// cuyo transform sigue haciendo falta.
        /// </summary>
        static void StripGeometry(GameObject go)
        {
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                Object.DestroyImmediate(mr);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                Object.DestroyImmediate(mf);
        }

        static RoboticArm BuildArm(bool right, MasterInput master, Transform field)
        {
            float sx = right ? 1f : -1f;
            var root = new GameObject(right ? "Arm_R" : "Arm_L");

            var trocar = new GameObject("Trocar").transform;
            trocar.SetParent(root.transform, false);
            trocar.position = new Vector3(sx * 0.32f, 1.62f, 0.98f);

            var trocarVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            trocarVis.name = "TrocarVis";
            Object.DestroyImmediate(trocarVis.GetComponent<Collider>());
            trocarVis.transform.SetParent(trocar, false);
            trocarVis.transform.localScale = Vector3.one * 0.05f;
            trocarVis.GetComponent<MeshRenderer>().sharedMaterial = mats["DarkMetal"];

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            Object.DestroyImmediate(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(root.transform, false);
            shaft.GetComponent<MeshRenderer>().sharedMaterial = mats["Metal"];

            var wrist = new GameObject("Wrist").transform;
            wrist.SetParent(root.transform, false);
            wrist.position = field.position + new Vector3(sx * 0.12f, 0.14f, -0.02f);

            var wristVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wristVis.name = "WristVis";
            Object.DestroyImmediate(wristVis.GetComponent<Collider>());
            wristVis.transform.SetParent(wrist, false);
            wristVis.transform.localScale = Vector3.one * 0.022f;
            wristVis.GetComponent<MeshRenderer>().sharedMaterial = mats["Metal"];

            Transform MakeJaw(string name)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(wrist, false);
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.transform.SetParent(pivot, false);
                vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                vis.transform.localPosition = new Vector3(0f, 0f, 0.032f);
                vis.transform.localScale = new Vector3(0.006f, 0.032f, 0.006f);
                vis.GetComponent<MeshRenderer>().sharedMaterial = mats["Metal"];
                return pivot;
            }
            var jawA = MakeJaw("JawA");
            var jawB = MakeJaw("JawB");

            var anchor = new GameObject("JawAnchor").transform;
            anchor.SetParent(wrist, false);
            anchor.localPosition = new Vector3(0f, 0f, 0.06f);

            var arm = root.AddComponent<RoboticArm>();
            arm.master = master;
            arm.trocar = trocar;
            arm.shaft = shaft.transform;
            arm.wrist = wrist;
            arm.jawA = jawA;
            arm.jawB = jawB;
            arm.jawAnchor = anchor;
            arm.workspaceCenter = field.position + Vector3.up * 0.15f;

            // --- Arte: pinza importada de SuturingSimulator ---------------
            // Sustituye SOLO lo visual. jawA/jawB siguen asignados en el brazo
            // (RoboticArm los mueve igual), pero se ocultan sus cilindros: así
            // la lógica de agarre, que usa jawAnchor, no cambia en nada.
            var instrumentPrefab = art == null ? null
                                 : (right ? art.instrumentRight : art.instrumentLeft);
            if (instrumentPrefab != null)
            {
                var visual = ArtSet.AttachVisual(instrumentPrefab, wrist,
                                                 art.instrumentScale, art.instrumentEuler,
                                                 art.instrumentOffset);
                if (visual != null)
                {
                    var iv = visual.gameObject.AddComponent<InstrumentVisual>();
                    iv.arm = arm;

                    // Llevar jawAnchor a las mandíbulas VISIBLES del modelo.
                    //
                    // jawAnchor es el punto desde el que mide toda la lógica
                    // (RoboticArm.TipPos: agarre de la aguja y captura de
                    // puntos de sutura). Estaba a 6 cm por delante de la
                    // muñeca, que es donde acababan las mandíbulas
                    // procedurales; pero las del modelo importado caen casi
                    // sobre la muñeca. Resultado: 70 mm entre lo que se ve y
                    // lo que se mide, así que tocar un punto era imposible por
                    // mucho que se acercara.
                    //
                    // Se identifica la pieza móvil por su blendshape, igual que
                    // hace InstrumentVisual.
                    foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0) continue;

                        // Centro de las mandíbulas visibles. Intenté afinar esto
                        // buscando "la punta" proyectando sobre wrist.forward,
                        // y salió PEOR (de 25-40 mm a 52-77 mm): el Clamp no se
                        // extiende hacia adelante sino de lado, así que esa
                        // proyección movía el ancla en perpendicular a las
                        // mandíbulas. El centro queda dentro de la pieza visible,
                        // que es lo que importa para el agarre.
                        //
                        // La captura de puntos de sutura YA NO usa este ancla:
                        // mide contra la superficie visible (ver SutureRig).
                        anchor.position = smr.bounds.center;
                        break;
                    }

                    // Apagar las primitivas que el modelo reemplaza, salvo en
                    // modo diagnóstico, donde se dejan para comparar posiciones.
                    if (!art.showProceduralGuides)
                    {
                        wristVis.SetActive(false);
                        foreach (var jaw in new[] { jawA, jawB })
                            foreach (var mr in jaw.GetComponentsInChildren<MeshRenderer>(true))
                                mr.enabled = false;

                        // El vástago y la bola del trócar también.
                        //
                        // Son de las primeras versiones, de cuando el
                        // instrumento entero era geometría procedural. Con la
                        // pinza importada puesta quedaban ADEMÁS del modelo, y
                        // se veían como dos tubos cruzando el campo que no
                        // respondían a nada: la sensación era de tener
                        // herramienta doble.
                        //
                        // Se quita la GEOMETRÍA, no el objeto: RoboticArm sigue
                        // colocando y escalando el vástago cada frame para el
                        // pivote en trócar, y desactivar el GameObject rompería
                        // esa lógica. Se destruyen los componentes en vez de
                        // apagar el renderer para que no haya forma de que algo
                        // los vuelva a encender.
                        StripGeometry(shaft);
                        StripGeometry(trocarVis);
                    }
                }
            }

            // Retroalimentación háptica de fuerza en el instrumento derecho
            // (activa de verdad cuando esa mano usa el Touch; en Quest vibra).
            if (right)
            {
                var haptics = root.AddComponent<InstrumentHaptics>();
                haptics.arm = arm;
                haptics.master = master;   // el tejido se autolocaliza al cargar el nivel
                haptics.enableTissue = true;
                haptics.enableTrocar = true;
            }
            return arm;
        }

        // ------------------------------------------------------------------
        static TextMeshPro MakeText(Transform parent, string name, string content,
            float size, Vector3 localPos, Vector2 rect, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<TextMeshPro>();
            t.text = content;
            t.fontSize = size;
            t.enableAutoSizing = true;      // nunca desborda su rectángulo
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.25f;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.rectTransform.sizeDelta = rect;
            return t;
        }

        static GameObject MakePanelQuad(Transform parent, Vector3 localPos, Vector2 size)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backing";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mats["Panel"];
            return quad;
        }

        static HUD BuildHUD()
        {
            var root = new GameObject("HUD");

            // Colocado para la mirada de trabajo, no para la de reposo.
            //
            // A 1.62 m de alto quedaba a la altura de los ojos de quien mira al
            // FRENTE, pero suturar obliga a mirar abajo, al campo (1.08 m, a
            // 0.55 m): en la grabación del 28-08 solo asomaba el borde inferior
            // del panel, recortado por arriba, así que la guía de fases —que es
            // la que explica la mecánica del punto— no se leía nunca.
            //
            // Ahora se pone más bajo y más lejos, pasada la herida, e inclinado
            // hacia arriba para que mire a los ojos: cae en el mismo vistazo
            // que el campo sin taparlo.
            //
            // Primer intento fue a 1.0 m y a tamaño completo, y se comía media
            // pantalla: un panel de 1.5 m visto a un metro abarca 60°. A 1.55 m
            // y al 68% mide unos 37° de ancho y queda entre 5° y 18° por debajo
            // del horizonte, mientras el campo está a unos 43°: se leen los dos
            // sin que uno estorbe al otro.
            //
            // Se escala la raíz en vez de recortar el panel para no rehacer la
            // colocación de los textos, que está medida contra 1.5 x 0.5.
            root.transform.position = new Vector3(0f, 1.28f, 1.55f);
            root.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            root.transform.localScale = Vector3.one * 0.68f;

            // 0.66 de alto, no 0.50: el aviso del pie caía en y = -0.26 con un
            // panel que acababa en -0.25, o sea FUERA. Sin fondo detrás y con
            // un rectángulo de solo 0.10 para textos largos, el mensaje se
            // desbordaba y cruzaba la pantalla en diagonal, como se ve en el
            // vídeo del 28-08.
            MakePanelQuad(root.transform, Vector3.zero, new Vector2(1.5f, 0.66f));
            var hud = root.AddComponent<HUD>();
            hud.titleText = MakeText(root.transform, "Title", "", 0.8f,
                new Vector3(0f, 0.17f, -0.005f), new Vector2(1.4f, 0.12f), new Color(0.65f, 0.85f, 1f));
            hud.objectiveText = MakeText(root.transform, "Objective", "", 0.55f,
                new Vector3(0f, 0.02f, -0.005f), new Vector2(1.4f, 0.18f), Color.white);
            hud.timerText = MakeText(root.transform, "Timer", "00:00", 0.5f,
                new Vector3(0f, -0.15f, -0.005f), new Vector2(1.4f, 0.08f), new Color(0.8f, 0.8f, 0.8f));
            // Rectángulo más alto para que los avisos largos ("Entrada correcta ·
            // gira la muñeca siguiendo la curva de la aguja") quepan en dos
            // líneas en vez de desbordar a lo ancho.
            hud.flashText = MakeText(root.transform, "Flash", "", 0.6f,
                new Vector3(0f, -0.24f, -0.005f), new Vector2(1.4f, 0.14f), Color.green);
            return hud;
        }

        static WorldButton MakeButton(Transform parent, string label, Vector3 localPos, string scene)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Btn_{label}";
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPos;
            cube.transform.localScale = new Vector3(0.42f, 0.09f, 0.03f);
            cube.GetComponent<MeshRenderer>().sharedMaterial = mats["ButtonIdle"];
            var btn = cube.AddComponent<WorldButton>();
            btn.sceneToLoad = scene;
            // el texto no puede heredar la escala del cubo: lo colocamos como hermano
            var txt = MakeText(parent, $"Lbl_{label}", label, 0.42f,
                localPos + new Vector3(0f, 0f, -0.025f), new Vector2(0.44f, 0.08f), Color.white);
            return btn;
        }

        static ResultsPanel BuildResultsPanel()
        {
            var root = new GameObject("ResultsPanel");
            root.transform.position = new Vector3(0f, 1.45f, 0.95f);

            MakePanelQuad(root.transform, Vector3.zero, new Vector2(1.1f, 0.85f));
            var panel = root.AddComponent<ResultsPanel>();
            panel.starOn = mats["StarOn"];
            panel.starOff = mats["StarOff"];

            panel.titleText = MakeText(root.transform, "Title", "Resultados", 0.8f,
                new Vector3(0f, 0.33f, -0.005f), new Vector2(1f, 0.12f), new Color(1f, 0.9f, 0.5f));

            panel.starRenderers = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.name = $"Star_{i}";
                Object.DestroyImmediate(star.GetComponent<Collider>());
                star.transform.SetParent(root.transform, false);
                star.transform.localPosition = new Vector3(-0.14f + i * 0.14f, 0.2f, -0.02f);
                star.transform.localScale = Vector3.one * 0.07f;
                panel.starRenderers[i] = star.GetComponent<MeshRenderer>();
                panel.starRenderers[i].sharedMaterial = mats["StarOff"];
            }

            panel.statsText = MakeText(root.transform, "Stats", "", 0.5f,
                new Vector3(0f, -0.02f, -0.005f), new Vector2(1f, 0.3f), Color.white);

            panel.retryButton = MakeButton(root.transform, "Reintentar", new Vector3(-0.33f, -0.32f, -0.01f), "");
            panel.nextButton = MakeButton(root.transform, "Siguiente", new Vector3(0.12f, -0.32f, -0.01f), "");
            panel.menuButton = MakeButton(root.transform, "Menú", new Vector3(0.45f, -0.32f, -0.01f), "");
            return panel;
        }

        static void BuildMenu()
        {
            var root = new GameObject("Menu");
            root.transform.position = new Vector3(0f, 1.5f, 1.6f);

            // 1.35 de alto, no 1.25: el sexto botón baja hasta y = -0.475 y con
            // el panel corto la ayuda del pie se le montaba encima.
            MakePanelQuad(root.transform, Vector3.zero, new Vector2(1.6f, 1.35f));

            MakeText(root.transform, "Title", "ENTRENADOR DE SUTURA ROBÓTICA", 1.1f,
                new Vector3(0f, 0.5f, -0.005f), new Vector2(1.5f, 0.16f), new Color(0.65f, 0.85f, 1f));
            MakeText(root.transform, "Sub", "Apunta con el controlador derecho y pulsa el gatillo", 0.5f,
                new Vector3(0f, 0.37f, -0.005f), new Vector2(1.5f, 0.08f), new Color(0.7f, 0.7f, 0.75f));

            string[] labels =
            {
                "1 · Manejo de aguja",
                "2 · Precisión en anillos",
                "3 · Punto simple",
                "4 · Sutura continua",
                "5 · Nudo intracorpóreo"
            };
            for (int i = 0; i < 5; i++)
                MakeButton(root.transform, labels[i],
                    new Vector3(0f, 0.22f - i * 0.13f, -0.01f), GameFlow.Scenes[i + 1]);

            // Escena de órgano real. Cuelga del menú como sexto botón pero no
            // entra en GameFlow.Scenes: no es un peldaño del temario, así que
            // no debe aparecer como "siguiente nivel" al terminar el 5.
            MakeButton(root.transform, "6 · Sutura en órgano real",
                new Vector3(0f, 0.22f - 5 * 0.13f, -0.01f), GameFlow.OrganScene);

            MakeText(root.transform, "Help",
                "Controles: gatillo = cerrar mandíbulas · mantener B/Y IZQUIERDO = salir al menú",
                0.4f, new Vector3(0f, -0.56f, -0.005f), new Vector2(1.5f, 0.1f), new Color(0.6f, 0.6f, 0.65f));
        }
    }
}
