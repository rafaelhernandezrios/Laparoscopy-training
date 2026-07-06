using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Almohadilla de tejido con herida central. Malla procedural que se
    /// deforma bajo la punta de la aguja y se recupera. Detecta punciones
    /// fuera de las dianas activas (error).
    /// </summary>
    public class TissuePatch : MonoBehaviour
    {
        public float width = 0.55f;
        public float depth = 0.38f;
        public int resX = 34;
        public int resZ = 24;
        public Material tissueMaterial;
        public Material woundMaterial;

        [HideInInspector] public SutureNeedle trackedNeedle;
        [HideInInspector] public bool punctureAllowed; // true mientras la aguja está pasando por dianas válidas

        public System.Action<Vector3> onBadPuncture;

        Mesh mesh;
        Vector3[] baseVerts, verts;
        float lastBadPuncture = -10f;

        public float SurfaceY => transform.position.y;
        public Vector3 WoundCenter => transform.position;
        public Vector3 WoundDir => transform.right; // la herida corre a lo largo de X local

        void Awake()
        {
            BuildMesh();
            BuildWoundLine();
            var box = gameObject.GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.02f, 0f);
            box.size = new Vector3(width, 0.04f, depth);
        }

        void BuildMesh()
        {
            mesh = new Mesh { name = "Tissue" };
            int vc = (resX + 1) * (resZ + 1);
            baseVerts = new Vector3[vc];
            verts = new Vector3[vc];
            var uvs = new Vector2[vc];
            int vi = 0;
            for (int z = 0; z <= resZ; z++)
                for (int x = 0; x <= resX; x++)
                {
                    float fx = (float)x / resX - 0.5f;
                    float fz = (float)z / resZ - 0.5f;
                    // leve abombamiento
                    float y = Mathf.Cos(fx * Mathf.PI) * Mathf.Cos(fz * Mathf.PI) * 0.012f;
                    baseVerts[vi] = new Vector3(fx * width, y, fz * depth);
                    uvs[vi] = new Vector2((float)x / resX, (float)z / resZ);
                    vi++;
                }
            var tris = new int[resX * resZ * 6];
            int ti = 0;
            for (int z = 0; z < resZ; z++)
                for (int x = 0; x < resX; x++)
                {
                    int a = z * (resX + 1) + x;
                    int b = a + resX + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = b; tris[ti++] = b + 1; tris[ti++] = a + 1;
                }
            System.Array.Copy(baseVerts, verts, vc);
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateNormals();

            var mf = gameObject.GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gameObject.GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = tissueMaterial != null ? tissueMaterial :
                (MaterialSet.I != null ? MaterialSet.I.tissue : null);
        }

        void BuildWoundLine()
        {
            var wound = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wound.name = "Wound";
            Object.Destroy(wound.GetComponent<Collider>());
            wound.transform.SetParent(transform, false);
            wound.transform.localPosition = new Vector3(0f, 0.013f, 0f);
            wound.transform.localScale = new Vector3(width * 0.6f, 0.004f, 0.008f);
            var mr = wound.GetComponent<MeshRenderer>();
            if (woundMaterial != null) mr.sharedMaterial = woundMaterial;
            else if (MaterialSet.I != null) mr.sharedMaterial = MaterialSet.I.wound;
        }

        void Update()
        {
            if (trackedNeedle == null) { Recover(); return; }
            Vector3 tip = trackedNeedle.TipPos;
            Vector3 local = transform.InverseTransformPoint(tip);
            bool overPad = Mathf.Abs(local.x) < width * 0.5f && Mathf.Abs(local.z) < depth * 0.5f;

            if (overPad && local.y < 0.02f)
            {
                Deform(local);
                if (local.y < -0.008f && !punctureAllowed && Time.time - lastBadPuncture > 1.5f)
                {
                    lastBadPuncture = Time.time;
                    onBadPuncture?.Invoke(tip);
                }
            }
            else Recover();
            mesh.vertices = verts;
            mesh.RecalculateNormals();
        }

        void Deform(Vector3 localTip)
        {
            float r = 0.06f;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector2 d2 = new Vector2(baseVerts[i].x - localTip.x, baseVerts[i].z - localTip.z);
                float d = d2.magnitude;
                float target = baseVerts[i].y;
                if (d < r)
                {
                    float g = Mathf.Exp(-(d * d) / (r * r * 0.25f));
                    float press = Mathf.Clamp(0.02f - localTip.y, 0f, 0.03f);
                    target = baseVerts[i].y - press * g;
                }
                verts[i].y = Mathf.Lerp(verts[i].y, target, 0.35f);
                verts[i].x = baseVerts[i].x; verts[i].z = baseVerts[i].z;
            }
        }

        void Recover()
        {
            if (verts == null) return;
            for (int i = 0; i < verts.Length; i++)
                verts[i] = Vector3.Lerp(verts[i], baseVerts[i], 0.12f);
            if (mesh != null) { mesh.vertices = verts; mesh.RecalculateNormals(); }
        }
    }
}
