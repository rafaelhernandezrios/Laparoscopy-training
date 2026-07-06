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

        static Dictionary<string, Material> mats;

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

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene($"{SceneDir}/{GameFlow.Scenes[0]}.unity");
            Debug.Log("[SutureTrainer] Escenas construidas y añadidas a Build Settings. ¡Listo!");
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
            BuildEnvironment();
            var camOffset = BuildXRRig();
            var field = BuildSurgicalField();

            // gestor central
            var mgr = new GameObject("GameManager");
            var matSet = mgr.AddComponent<MaterialSet>();
            WireMaterialSet(matSet);

            var masterR = mgr.AddComponent<MasterInput>();
            masterR.node = XRNode.RightHand; masterR.trackingSpace = camOffset;
            var masterL = mgr.AddComponent<MasterInput>();
            masterL.node = XRNode.LeftHand; masterL.trackingSpace = camOffset;

            var laser = mgr.AddComponent<ControllerLaser>();
            laser.master = masterR;
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

                var hud = BuildHUD();
                var results = BuildResultsPanel();

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

        static void BuildEnvironment()
        {
            var env = new GameObject("Environment");

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
            cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
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

            return offset.transform;
        }

        static Transform BuildSurgicalField()
        {
            var field = new GameObject("SurgicalField");
            field.transform.position = new Vector3(0f, 1.08f, 0.55f);

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "FieldBase";
            table.transform.SetParent(field.transform, false);
            table.transform.localPosition = new Vector3(0f, -0.035f, 0f);
            table.transform.localScale = new Vector3(0.8f, 0.06f, 0.6f);
            table.GetComponent<MeshRenderer>().sharedMaterial = mats["Metal"];

            return field.transform;
        }

        // ------------------------------------------------------------------
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
            root.transform.position = new Vector3(0f, 1.62f, 1.25f);

            MakePanelQuad(root.transform, Vector3.zero, new Vector2(1.5f, 0.5f));
            var hud = root.AddComponent<HUD>();
            hud.titleText = MakeText(root.transform, "Title", "", 1.6f,
                new Vector3(0f, 0.17f, -0.005f), new Vector2(1.4f, 0.14f), new Color(0.65f, 0.85f, 1f));
            hud.objectiveText = MakeText(root.transform, "Objective", "", 1.0f,
                new Vector3(0f, 0.02f, -0.005f), new Vector2(1.4f, 0.2f), Color.white);
            hud.timerText = MakeText(root.transform, "Timer", "00:00", 0.9f,
                new Vector3(0f, -0.15f, -0.005f), new Vector2(1.4f, 0.1f), new Color(0.8f, 0.8f, 0.8f));
            hud.flashText = MakeText(root.transform, "Flash", "", 1.1f,
                new Vector3(0f, -0.28f, -0.005f), new Vector2(1.4f, 0.12f), Color.green);
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
            var txt = MakeText(parent, $"Lbl_{label}", label, 0.8f,
                localPos + new Vector3(0f, 0f, -0.025f), new Vector2(0.5f, 0.09f), Color.white);
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

            panel.titleText = MakeText(root.transform, "Title", "Resultados", 1.5f,
                new Vector3(0f, 0.33f, -0.005f), new Vector2(1f, 0.14f), new Color(1f, 0.9f, 0.5f));

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

            panel.statsText = MakeText(root.transform, "Stats", "", 0.9f,
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

            MakePanelQuad(root.transform, Vector3.zero, new Vector2(1.6f, 1.25f));

            MakeText(root.transform, "Title", "ENTRENADOR DE SUTURA ROBÓTICA", 2.2f,
                new Vector3(0f, 0.5f, -0.005f), new Vector2(1.5f, 0.2f), new Color(0.65f, 0.85f, 1f));
            MakeText(root.transform, "Sub", "Apunta con el controlador derecho y pulsa el gatillo", 0.9f,
                new Vector3(0f, 0.37f, -0.005f), new Vector2(1.5f, 0.1f), new Color(0.7f, 0.7f, 0.75f));

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

            MakeText(root.transform, "Help",
                "Controles: gatillo = cerrar mandíbulas · botón A/X = clutch (reposicionar sin mover el instrumento)",
                0.7f, new Vector3(0f, -0.53f, -0.005f), new Vector2(1.5f, 0.12f), new Color(0.6f, 0.6f, 0.65f));
        }
    }
}
