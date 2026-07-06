using UnityEngine;

namespace SutureTrainer
{
    /// <summary>Generación procedural de mallas: toro (anillos) y tubo en arco (aguja).</summary>
    public static class ProcMesh
    {
        public static Mesh Torus(float ringRadius, float tubeRadius, int segR = 32, int segT = 12)
        {
            var mesh = new Mesh { name = "Torus" };
            int vc = (segR + 1) * (segT + 1);
            var verts = new Vector3[vc];
            var norms = new Vector3[vc];
            int vi = 0;
            for (int i = 0; i <= segR; i++)
            {
                float u = (float)i / segR * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) * ringRadius;
                Vector3 cd = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= segT; j++)
                {
                    float v = (float)j / segT * Mathf.PI * 2f;
                    Vector3 n = cd * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                    verts[vi] = c + n * tubeRadius;
                    norms[vi] = n;
                    vi++;
                }
            }
            var tris = new int[segR * segT * 6];
            int ti = 0;
            for (int i = 0; i < segR; i++)
                for (int j = 0; j < segT; j++)
                {
                    int a = i * (segT + 1) + j;
                    int b = a + segT + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = b; tris[ti++] = b + 1; tris[ti++] = a + 1;
                }
            mesh.vertices = verts; mesh.normals = norms; mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Tubo a lo largo de un arco circular en el plano XY local, centrado en el origen.
        /// El arco va de -arcDeg/2 a +arcDeg/2. Devuelve además puntos inicial (cola/swage)
        /// y final (punta) con sus tangentes.
        /// </summary>
        public static Mesh ArcTube(float arcRadius, float arcDeg, float tubeRadius,
            out Vector3 tailPos, out Vector3 tailTan, out Vector3 tipPos, out Vector3 tipTan,
            int segA = 24, int segT = 8, bool taperTip = true)
        {
            var mesh = new Mesh { name = "ArcTube" };
            float a0 = (-arcDeg * 0.5f) * Mathf.Deg2Rad;
            float a1 = (arcDeg * 0.5f) * Mathf.Deg2Rad;
            int vc = (segA + 1) * (segT + 1);
            var verts = new Vector3[vc];
            var norms = new Vector3[vc];
            int vi = 0;
            for (int i = 0; i <= segA; i++)
            {
                float t = (float)i / segA;
                float ang = Mathf.Lerp(a0, a1, t);
                Vector3 c = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * arcRadius;
                Vector3 radial = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                float r = tubeRadius;
                if (taperTip && t > 0.8f) r = tubeRadius * Mathf.Lerp(1f, 0.05f, (t - 0.8f) / 0.2f);
                for (int j = 0; j <= segT; j++)
                {
                    float v = (float)j / segT * Mathf.PI * 2f;
                    Vector3 n = radial * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                    verts[vi] = c + n * r;
                    norms[vi] = n;
                    vi++;
                }
            }
            var tris = new int[segA * segT * 6];
            int ti = 0;
            for (int i = 0; i < segA; i++)
                for (int j = 0; j < segT; j++)
                {
                    int a = i * (segT + 1) + j;
                    int b = a + segT + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = b; tris[ti++] = b + 1; tris[ti++] = a + 1;
                }
            mesh.vertices = verts; mesh.normals = norms; mesh.triangles = tris;
            mesh.RecalculateBounds();

            tailPos = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * arcRadius;
            tipPos = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * arcRadius;
            tailTan = new Vector3(-Mathf.Sin(a0), Mathf.Cos(a0), 0f) * -1f; // hacia fuera del arco
            tipTan = new Vector3(-Mathf.Sin(a1), Mathf.Cos(a1), 0f);        // dirección de avance de la punta
            return mesh;
        }
    }
}
