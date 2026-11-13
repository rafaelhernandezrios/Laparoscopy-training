using System.Text;
using UnityEditor;
using UnityEngine;

namespace SutureTrainer.EditorTools
{
    /// <summary>
    /// Comprueba si la herida del órgano PUEDE abrirse y cerrarse.
    ///
    /// Dos condiciones tienen que cumplirse y no son evidentes mirando la
    /// escena:
    ///
    /// 1. Los labios deben estar SEPARADOS en la malla en reposo. Si el modelo
    ///    viene con la incisión cerrada, la herida nace cerrada y suturar solo
    ///    puede deformarla.
    ///
    /// 2. Los vértices de los labios NO deben estar anclados. DeformableMesh
    ///    marca isFixed = color.rgb < 0.5 a partir del color de vértice; un
    ///    vértice anclado no se mueve nunca, así que si los labios lo están, la
    ///    herida no podrá cerrarse por mucho que se tense.
    /// </summary>
    public static class OrganWoundCheck
    {
        [MenuItem("SutureTrainer/Diagnóstico: ¿puede cerrarse la herida?")]
        public static void Run()
        {
            var organ = Object.FindFirstObjectByType<OrganRig>();
            if (organ == null)
            {
                Debug.LogWarning("[HeridaCheck] No hay OrganRig en la escena abierta. " +
                                 "Abre ST_03/04/05 o ST_06.");
                return;
            }

            var deform = organ.deformable;
            var mf = deform != null ? deform.meshFilter : null;
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null)
            {
                Debug.LogError("[HeridaCheck] El órgano no tiene malla.");
                return;
            }

            var verts = mesh.vertices;
            var colors = mesh.colors;
            bool hayColores = colors != null && colors.Length == verts.Length;

            var sb = new StringBuilder();
            sb.AppendLine($"[HeridaCheck] {organ.PairCount} pares · malla de {verts.Length} vértices · " +
                          $"colores de vértice: {(hayColores ? "sí" : "NO (todos quedarían libres)")}");

            bool algunAnclado = false;
            float sumaHueco = 0f;

            for (int i = 0; i < organ.PairCount; i++)
            {
                var e = organ.pairs[i].entry;
                var x = organ.pairs[i].exit;
                if (e == null || x == null) continue;

                int ie = e.vertexIndex, ix = x.vertexIndex;
                if (ie < 0 || ie >= verts.Length || ix < 0 || ix >= verts.Length) continue;

                float hueco = Vector3.Distance(verts[ie], verts[ix]);
                sumaHueco += hueco;

                string estado = "";
                if (hayColores)
                {
                    bool fe = Fijo(colors[ie]), fx = Fijo(colors[ix]);
                    if (fe || fx) algunAnclado = true;
                    estado = $"  anclados: entrada={(fe ? "SÍ" : "no")} salida={(fx ? "SÍ" : "no")}";
                }

                sb.AppendLine($"   par {i}: hueco en reposo = {hueco * 1000f:F1} mm{estado}");
            }

            sb.AppendLine();
            sb.AppendLine($"   hueco medio: {(organ.PairCount > 0 ? sumaHueco / organ.PairCount * 1000f : 0f):F1} mm " +
                          $"-> la herida nace {(sumaHueco > 0.001f ? "ABIERTA (correcto)" : "CERRADA (mal)")}");

            // ¿La malla tiene una ABERTURA real o es continua con el corte
            // solo pintado en la textura? Se mira si hay vértices ocupando la
            // franja entre los dos labios: si el tejido está cortado, ahí no
            // debería haber nada.
            AnalizarFranja(organ, verts, sb);

            if (algunAnclado)
                sb.AppendLine("   AVISO: hay labios ANCLADOS por color de vértice. Esos no se " +
                              "moverán nunca, así que la herida no podrá cerrarse ahí.");
            else if (hayColores)
                sb.AppendLine("   Ningún labio está anclado: pueden aproximarse al tensar.");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Cuenta cuántos vértices caen en la franja entre los dos labios.
        ///
        /// Si el tejido está realmente abierto, esa franja está vacía. Si la
        /// malla es continua y el corte solo está pintado en la textura, la
        /// franja estará llena de vértices — y entonces "abrir la herida" no es
        /// cuestión de tensar sino de que el modelo nunca tuvo abertura.
        /// </summary>
        static void AnalizarFranja(OrganRig organ, Vector3[] verts, System.Text.StringBuilder sb)
        {
            if (organ.PairCount == 0) return;

            // Eje que cruza la herida: de entrada a salida del primer par.
            Vector3 a = organ.pairs[0].entry.transform.position;
            Vector3 b = organ.pairs[0].exit.transform.position;
            Vector3 cruce = (b - a).normalized;
            Vector3 medio = (a + b) * 0.5f;
            float mitad = Vector3.Distance(a, b) * 0.5f;

            // Franja central: el 40% del ancho, centrada en la línea de corte.
            float banda = mitad * 0.4f;

            var mf = organ.deformable != null ? organ.deformable.meshFilter : null;
            Transform t = mf != null ? mf.transform : organ.transform;

            int dentro = 0;
            foreach (var v in verts)
            {
                Vector3 w = t.TransformPoint(v);
                float d = Vector3.Dot(w - medio, cruce);
                if (Mathf.Abs(d) > banda) continue;
                // y que esté a lo largo de la incisión, no en otra parte del órgano
                if (Vector3.Distance(w, medio) > mitad * 3f) continue;
                dentro++;
            }

            sb.AppendLine();
            sb.AppendLine($"   vértices en la franja central de la herida: {dentro}");
            sb.AppendLine(dentro == 0
                ? "   -> franja VACÍA: la malla tiene abertura real."
                : "   -> franja OCUPADA: la malla es CONTINUA y el corte solo está " +
                  "pintado en la textura. Tensar juntará vértices, pero la abertura " +
                  "visible depende del dibujo, no de la geometría.");
        }

        static bool Fijo(Color c) => c.r < 0.5f && c.g < 0.5f && c.b < 0.5f;
    }
}
