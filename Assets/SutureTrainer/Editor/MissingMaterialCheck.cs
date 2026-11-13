using System.Text;
using UnityEditor;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Lista los renderers de la escena abierta a los que les falta material.
    ///
    /// Unity los pinta de magenta sin decir nada más, y en el visor es
    /// imposible saber si eso es un shader incompatible, una textura perdida o
    /// simplemente un hueco en sharedMaterials. Casi siempre es lo tercero:
    /// una ruta que no resolvió al construir la escena.
    /// </summary>
    public static class MissingMaterialCheck
    {
        [MenuItem("SutureTrainer/Diagnóstico: ¿qué sale magenta?")]
        public static void Run()
        {
            var sb = new StringBuilder();
            int rotos = 0;

            foreach (var r in Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials;

                if (mats == null || mats.Length == 0)
                {
                    sb.AppendLine($"  {Ruta(r.transform)} — sin materiales");
                    rotos++;
                    continue;
                }

                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                    {
                        sb.AppendLine($"  {Ruta(r.transform)} — hueco en slot {i} " +
                                      $"de {mats.Length}");
                        rotos++;
                    }
                    else if (mats[i].shader == null || !mats[i].shader.isSupported)
                    {
                        sb.AppendLine($"  {Ruta(r.transform)} — slot {i}: " +
                                      $"'{mats[i].name}' con shader no soportado " +
                                      $"({(mats[i].shader != null ? mats[i].shader.name : "null")})");
                        rotos++;
                    }
                }

                // Más submallas que materiales: las sobrantes también salen
                // magenta, y esto no se ve en el inspector.
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null &&
                    mf.sharedMesh.subMeshCount > mats.Length)
                {
                    sb.AppendLine($"  {Ruta(r.transform)} — {mf.sharedMesh.subMeshCount} " +
                                  $"submallas pero solo {mats.Length} materiales");
                    rotos++;
                }
            }

            if (rotos == 0)
                Debug.Log("[Magenta] Ningún renderer sin material en la escena abierta.");
            else
                Debug.LogWarning($"[Magenta] {rotos} problema(s):\n{sb}");
        }

        static string Ruta(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
