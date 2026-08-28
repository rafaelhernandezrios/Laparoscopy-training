using UnityEngine;
using UnityEditor;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Monta el quirófano importado (glTF) alrededor del campo quirúrgico.
    ///
    /// El modelo es de arquitectura, no de gameplay: entra tal cual, sin
    /// colisionadores y marcado estático, y es la SALA la que se mueve para que
    /// su mesa de operaciones caiga bajo el campo ya calibrado. Nunca al revés:
    /// las cotas de los ejercicios (campo a 1.08 m, trócares a 1.62 m) están
    /// medidas y no se tocan por decorar.
    ///
    /// Como todo el arte del proyecto, es opcional: si el .glb no está o
    /// glTFast no lo ha importado todavía, Build() devuelve un resultado vacío
    /// y SceneBuilder cae al suelo y la consola procedurales de siempre.
    /// </summary>
    public static class OperatingRoomBuilder
    {
        public const string ModelPath =
            "Assets/SutureTrainer/Art/Environment/OperatingRoom.glb";

        /// <summary>Resultado de colocar la sala.</summary>
        public struct Placement
        {
            /// <summary>false si no había modelo que montar.</summary>
            public bool built;
            /// <summary>Altura del tablero de la mesa de operaciones, en metros.</summary>
            public float tableTopY;
        }

        /// <summary>
        /// Mete la sala en la escena ya abierta, sin reconstruirla. Es la vía
        /// para vestir una escena que se haya retocado a mano, porque
        /// SceneBuilder la regeneraría desde cero y se llevaría los ajustes por
        /// delante.
        /// </summary>
        [MenuItem("SutureTrainer/Añadir quirófano a la escena abierta")]
        public static void AddToOpenScene()
        {
            var field = GameObject.Find("SurgicalField");
            if (field == null)
            {
                Debug.LogError("[SutureTrainer] No hay SurgicalField en la escena " +
                               "abierta: sin él no se sabe dónde centrar la mesa.");
                return;
            }

            var env = GameObject.Find("Environment");
            if (env == null) env = new GameObject("Environment");

            var previous = env.transform.Find("OperatingRoom");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);

            var placement = Build(env.transform, field.transform.position);
            if (!placement.built) return;

            // El suelo y la consola procedurales sobran en cuanto la sala pone
            // los suyos, y la consola además se cruzaría con la mesa.
            foreach (var name in new[] { "Floor", "ConsoleDesk" })
            {
                var t = env.transform.Find(name);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            // La bandeja del campo se estira hasta el tablero para no quedarse
            // flotando sobre la mesa. Igual que en SceneBuilder, la cara
            // superior no se mueve: es el plano de trabajo calibrado.
            var basePlate = field.transform.Find("FieldBase");
            if (basePlate != null && !float.IsNaN(placement.tableTopY))
            {
                float top = basePlate.localPosition.y + basePlate.localScale.y * 0.5f;
                float height = field.transform.position.y + top - placement.tableTopY;
                if (height > basePlate.localScale.y)
                {
                    var s = basePlate.localScale; s.y = height;
                    basePlate.localScale = s;
                    var p = basePlate.localPosition; p.y = top - height * 0.5f;
                    basePlate.localPosition = p;
                }
            }

            EditorUtility.SetDirty(env);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        /// <summary>
        /// Instancia la sala bajo <paramref name="parent"/> y la desplaza (y si
        /// hace falta la gira 90°) para que la mesa de operaciones quede
        /// centrada en <paramref name="fieldPos"/>.
        /// </summary>
        public static Placement Build(Transform parent, Vector3 fieldPos)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.Log($"[SutureTrainer] Sin {ModelPath}: entorno procedural. " +
                          "Si el .glb está en disco, espera a que glTFast " +
                          "(com.unity.cloud.gltfast) termine de importarlo.");
                return default;
            }

            var room = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (room == null) room = Object.Instantiate(model);
            room.name = "OperatingRoom";
            room.transform.SetParent(parent, false);

            var content = ContentRoot(room.transform);

            // Suelo a y = 0. El rig usa TrackingOriginMode.Floor, así que el
            // suelo de la sala TIENE que estar en el cero o el usuario aparece
            // hundido o flotando. La losa se reconoce por ser la huella más
            // grande que remata a la altura del piso.
            var slab = FindFloorSlab(content);
            float floorY = slab.HasValue ? slab.Value.max.y : 0f;

            var table = FindOperatingTable(content);
            if (table == null)
            {
                Debug.LogWarning("[SutureTrainer] Quirófano montado pero no se " +
                                 "reconoció la mesa de operaciones: la sala queda " +
                                 "en su origen y puede no cuadrar con el campo.");
                room.transform.position += Vector3.down * floorY;
                Freeze(room);
                return new Placement { built = true, tableTopY = float.NaN };
            }

            var b = WorldBounds(table);

            // El cirujano se pone en el lado largo de la mesa, y en la escena
            // mira a +Z. Si el modelo trae la mesa atravesada, se gira la sala
            // entera sobre el eje vertical que pasa por el centro de la mesa.
            //
            // Girar sobre ESE eje deja invariante el centro de la mesa (y su
            // altura), así que las cotas medidas antes de rotar siguen valiendo
            // y no hace falta releer los bounds.
            if (b.size.z > b.size.x)
                room.transform.RotateAround(b.center, Vector3.up, 90f);

            room.transform.position += new Vector3(fieldPos.x - b.center.x,
                                                   -floorY,
                                                   fieldPos.z - b.center.z);

            float tableTopY = b.max.y - floorY;
            Freeze(room);

            Debug.Log($"[SutureTrainer] Quirófano montado: mesa de " +
                      $"{Mathf.Max(b.size.x, b.size.z):F2} x {Mathf.Min(b.size.x, b.size.z):F2} m, " +
                      $"tablero a {tableTopY:F2} m, centrada en el campo.");

            return new Placement { built = true, tableTopY = tableTopY };
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// Baja por la cadena de nodos de un solo hijo que el exportador de
        /// glTF cuelga por encima de la escena, hasta el nodo que de verdad
        /// agrupa los muebles.
        /// </summary>
        static Transform ContentRoot(Transform root)
        {
            var t = root;
            while (t.childCount == 1 && t.GetComponent<Renderer>() == null)
                t = t.GetChild(0);
            return t;
        }

        /// <summary>
        /// Losa del suelo: de entre los grupos que rematan a ras de piso, el de
        /// mayor huella.
        /// </summary>
        static Bounds? FindFloorSlab(Transform content)
        {
            Bounds? best = null;
            float bestArea = 0f;

            foreach (Transform child in content)
            {
                if (!TryWorldBounds(child, out var b)) continue;
                if (Mathf.Abs(b.max.y) > 0.3f) continue;

                float area = b.size.x * b.size.z;
                if (area > bestArea) { bestArea = area; best = b; }
            }
            return best;
        }

        /// <summary>
        /// Mesa de operaciones: apoyada en el suelo, con el tablero a altura de
        /// cadera y una huella alargada. Los carros y taburetes de la sala
        /// quedan fuera por ser demasiado cortos.
        /// </summary>
        static Transform FindOperatingTable(Transform content)
        {
            Transform best = null;
            float bestArea = 0f;

            foreach (Transform child in content)
            {
                if (!TryWorldBounds(child, out var b)) continue;

                if (b.min.y > 0.15f) continue;                       // no apoyada en el suelo
                if (b.max.y < 0.6f || b.max.y > 1.2f) continue;      // tablero fuera de rango
                float longSide = Mathf.Max(b.size.x, b.size.z);
                float shortSide = Mathf.Min(b.size.x, b.size.z);
                if (longSide < 1.4f || shortSide < 0.35f) continue;  // carro o taburete

                float area = longSide * shortSide;
                if (area > bestArea) { bestArea = area; best = child; }
            }
            return best;
        }

        static bool TryWorldBounds(Transform t, out Bounds bounds)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            bounds = default;
            if (renderers.Length == 0) return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        static Bounds WorldBounds(Transform t)
        {
            TryWorldBounds(t, out var b);
            return b;
        }

        /// <summary>
        /// La sala es decorado: sin colisionadores (nadie se desplaza en estas
        /// escenas) y estática, para que el batching combine sus ~340 mallas en
        /// lugar de gastar un draw call por mueble dentro del visor.
        /// </summary>
        static void Freeze(GameObject room)
        {
            foreach (var col in room.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);

            var flags = StaticEditorFlags.BatchingStatic |
                        StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.OccludeeStatic;

            foreach (var t in room.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }
    }
}
