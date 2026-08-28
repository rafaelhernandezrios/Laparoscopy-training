using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

public class DeformableMesh : MonoBehaviour
{
    public GameObject meshRoot;
    public MeshFilter meshFilter;
    private Mesh mesh;
    
    public float mass = 1.0f;
    public float stiffnessOrigin = 0.1f;
    public float stiffnessNeighbor = 0.1f;

    public float damping = 0.9f;
    
    private Vector3[] originalVertices;

    public SutureManager sutueManager;
    
    public Material staticMaterial;
    public Material dynamicMaterial;
    public Material sutureMaterial;
    public float weldTolerance = 0.0001f;
    public float maxVertexForce = 10f;
    public float maxVelocity = 10f;
    
    public List<int> selectedVertices;
    public Vector3 selectedForce;
    
    [ShowInInspector]
    public List<MasterVertex> masterVertices;
    [ShowInInspector]
    public Dictionary<int, int> vertexToMaster;
    
    public GameObject proxy;
    public bool moveProxy;
    void Start()
    {
        mesh = GetComponent<MeshFilter>().mesh;

        masterVertices = MeshMasterVertexBuilder.Build(
            mesh,
            weldTolerance,
            out vertexToMaster
        );

        for (int i = 0; i < masterVertices.Count; i++)
            masterVertices[i].isFixed = masterVertices[i].color.r < 0.5f 
                                        && masterVertices[i].color.g < 0.5f 
                                        && masterVertices[i].color.b < 0.5f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        AddForceToVertexGroup();
        SimulateMasterVertices(dt);
        ApplyMasterVerticesToMesh();
        
        // if(Input.GetMouseButton(0))
        // {
        //     selectedVertices[0]++;
        //     if(moveProxy)
        //         MoveProxyToSelected0();
        // }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        // AddForceToVertexGroup();
        // SimulateMasterVertices(dt);
        // ApplyMasterVerticesToMesh();
    }

    void SimulateMasterVertices(float dt)
    {
        foreach (MasterVertex v in masterVertices)
        {
            if (v.isFixed) continue;

            Vector3 force = Vector3.zero;

            // Fuerza de vecinos
            foreach (int neighborIndex in v.neighborMasterIndices)
            {
                MasterVertex n = masterVertices[neighborIndex];

                if((v.color.b > 0.5f || v.color.g > 0.5f) && n.isFixed)
                    continue;
                
                Vector3 dir = n.position - v.position;
                float dist = dir.magnitude;

                if (dist > 0.0001f)
                    force += dir.normalized * (dist * stiffnessNeighbor);
            }
            
            
            // fuerza de la sutura
            if(v.isSuture && sutueManager.sutureVertices != null && sutueManager.sutureVertices.Count > v.sutureIndex)
            {
                //print($"Surure {v.sutureIndex}");
                Vector3 sutureForce = Vector3.zero;
                Vector3 dir = sutueManager.sutureVertices[v.sutureIndex] - v.position;
                float dist = dir.magnitude;
                sutureForce = dir.normalized * (dist * sutueManager.stiffnessSuture);
                force += sutureForce;
            }
            

            // Fuerza restitutiva a la posición inicial
            Vector3 displacement = v.position - v.initialPosition;
            Vector3 restorativeForce = -displacement * stiffnessOrigin;

            force += restorativeForce;

            // Amortiguamiento
            force += -v.velocity * damping;

            if(force.magnitude > maxVertexForce)
                force = force.normalized * maxVertexForce;
            
            v.velocity += force * dt;
            
            if (v.velocity.magnitude > maxVelocity)
                v.velocity = v.velocity.normalized * maxVelocity;
            
            v.position += v.velocity * dt;
        }
    }
    
    
    void ApplyMasterVerticesToMesh()
    {
        Vector3[] vertices = mesh.vertices;

        foreach (MasterVertex mv in masterVertices)
        {
            foreach (int meshVertexIndex in mv.duplicateIndices)
            {
                vertices[meshVertexIndex] = mv.position;
            }
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void AddForceToVertexGroup()
    {
        foreach (int index in selectedVertices)
            masterVertices[index].velocity += (selectedForce/mass) * Time.deltaTime;
    }

    [Button]
    public void SutureVertices(GameObject _proxy, int _sutureIndex)
    {
        SelectVerticesBySphereProxy(_proxy);
        foreach (int sutIndex in selectedVertices)
        {
            masterVertices[sutIndex].isSuture = true;
            masterVertices[sutIndex].sutureIndex = _sutureIndex;
        }
    }
    
    [Button]
    public void SutureVertices(int vertexIndex, int _sutureIndex, int _neighborCount = 0)
    {
        // masterVertices[vertexIndex].isSuture = true;
        // masterVertices[vertexIndex].sutureIndex = _sutureIndex;

        Queue<(int index, int depth)> queue = new Queue<(int, int)>();
        HashSet<int> visited = new HashSet<int>();

        queue.Enqueue((vertexIndex, 0));
        visited.Add(vertexIndex);
        
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            MasterVertex v = masterVertices[current.index];

            v.isSuture = true;
            v.sutureIndex = _sutureIndex;

            if (current.depth >= _neighborCount)
                continue;

            foreach (int neighborIndex in v.neighborMasterIndices)
            {
                if (visited.Contains(neighborIndex))
                    continue;

                visited.Add(neighborIndex);
                queue.Enqueue((neighborIndex, current.depth + 1));
            }
        }
    }

    [Button]
    public void ResetSuturingPoints()
    {
        foreach (MasterVertex mv in masterVertices)
            mv.isSuture = false;
    }


    [Button]
    public void SelectVerticesByProxy()
    {
        SelectVerticesBySphereProxy(proxy);
    }
    
    [Button]
    public void SelectVerticesBySphereProxy(GameObject sphere)
    {
        SelectVerticesBySphereProxy(sphere.transform.position, sphere.transform.localScale.x * 0.5f);
    }
    
    [Button]
    public void SelectVerticesBySphereProxy(Vector3 _center, float radius)
    {
        Vector3 center = transform.InverseTransformPoint(_center);
        selectedVertices.Clear();
        //foreach (MasterVertex mv in masterVertices)
        for(int i = 0; i < masterVertices.Count; i++)
        {
            if (Vector3.Distance(masterVertices[i].position, center) <= radius)
            {
                //print($"Selecting {i}");
                //selectedVertices.AddRange(masterVertices[i].duplicateIndices);
                selectedVertices.Add(i);
            }
        }
    }

    [Button]
    public void MoveProxyToSelected0()
    {
        if (selectedVertices.Count == 0) return;
        proxy.transform.position = transform.InverseTransformPoint(masterVertices[selectedVertices[0]].position);
    }
}
    
    
[System.Serializable]
public class MasterVertex
{
    public int masterIndex;
    public Vector3 position;
    public Vector3 initialPosition;
    
    public bool isSuture;
    public int sutureIndex;
    
    public Color color;
    // Índices reales dentro de mesh.vertices que representan el mismo punto espacial
    public List<int> duplicateIndices = new List<int>();

    // Vecinos únicos, ya fusionando los vecinos de todos los duplicados
    public HashSet<int> neighborMasterIndices = new HashSet<int>();

    // Datos dinámicos
    public Vector3 velocity;
    public bool isFixed;

    public MasterVertex(int masterIndex, Vector3 position)
    {
        this.masterIndex = masterIndex;
        this.position = position;
        this.initialPosition = position;
    }
}

[System.Serializable]
public class MeshMasterVertexBuilder
{
    public static List<MasterVertex> Build(
        Mesh mesh,
        float mergeTolerance,
        out Dictionary<int, int> vertexToMaster)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        List<MasterVertex> masterVertices = new List<MasterVertex>();
        vertexToMaster = new Dictionary<int, int>();

        float sqrTolerance = mergeTolerance * mergeTolerance;

        // Crear vértices maestros
        for (int i = 0; i < vertices.Length; i++)
        {
            int foundMaster = -1;

            for (int m = 0; m < masterVertices.Count; m++)
            {
                if ((vertices[i] - masterVertices[m].position).sqrMagnitude <= sqrTolerance)
                {
                    foundMaster = m;
                    break;
                }
            }

            if (foundMaster == -1)
            {
                MasterVertex mv = new MasterVertex(masterVertices.Count, vertices[i]);
                mv.duplicateIndices.Add(i);

                masterVertices.Add(mv);
                vertexToMaster[i] = mv.masterIndex;
            }
            else
            {
                masterVertices[foundMaster].duplicateIndices.Add(i);
                vertexToMaster[i] = foundMaster;
            }
        }

        // Construir vecinos usando triángulos
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = vertexToMaster[triangles[i]];
            int b = vertexToMaster[triangles[i + 1]];
            int c = vertexToMaster[triangles[i + 2]];

            // AddNeighbor(masterVertices, a, b);
            // AddNeighbor(masterVertices, b, c);
            // AddNeighbor(masterVertices, c, a);
            
            AddTriangleNeighbors(masterVertices, a, b, c);
        }

        
        Color[] colors = mesh.colors;
        bool hasColors = colors != null && colors.Length == vertices.Length;
        
        if (hasColors)
        {
            foreach (MasterVertex mv in masterVertices)
            {
                Color accumulated = Color.black;

                foreach (int duplicateIndex in mv.duplicateIndices)
                {
                    accumulated += colors[duplicateIndex];
                }

                mv.color = accumulated / mv.duplicateIndices.Count;
            }
        }
        
        return masterVertices;
    }

    private static void AddNeighbor(List<MasterVertex> list, int a, int b)
    {
        if (a == b) return;

        list[a].neighborMasterIndices.Add(b);
        list[b].neighborMasterIndices.Add(a);
    }
    
    private static void AddTriangleNeighbors(
        List<MasterVertex> list,
        int a,
        int b,
        int c)
    {
        AddNeighbor(list, a, b);
        AddNeighbor(list, a, c);

        AddNeighbor(list, b, a);
        AddNeighbor(list, b, c);

        AddNeighbor(list, c, a);
        AddNeighbor(list, c, b);
    }
}




    // [FoldoutGroup("Vertex Lists")]public Vector3[] allVertices;
    // [FoldoutGroup("Vertex Lists")]public List<int> dynamicVertexIndices = new List<int>();
    // [FoldoutGroup("Vertex Lists")]public List<int> sutureVertexIndices = new List<int>();
    //
    // [ShowInInspector][FoldoutGroup("Vertex Lists")]
    // private Dictionary<int, List<int>> linkedVerticesByIndex =
    //     new Dictionary<int, List<int>>();
    //
    // public Vector3 sutureForce;
    //

    //     //4422
    // void Start2()
    // {
    //     mesh = meshFilter.mesh;
    //     mesh.MarkDynamic();
    //
    //     allVertices = mesh.vertices;
    //     originalVertices = mesh.vertices;
    //     velocities = new Vector3[allVertices.Length];
    //     
    //     BuildLinkedVerticesByIndex();
    //     
    //     FindVerticesByMaterial(sutureMaterial, out sutureVertexIndices);
    //     FindVerticesByMaterial(dynamicMaterial, out dynamicVertexIndices);
    //     dynamicVertexIndices.AddRange(sutureVertexIndices);
    // }
    //
    //
    // private void BuildLinkedVerticesByIndex()
    // {
    //     linkedVerticesByIndex.Clear();
    //
    //     Dictionary<Vector3Int, List<int>> positionMap = new Dictionary<Vector3Int, List<int>>();
    //
    //     Vector3[] verts = mesh.vertices;
    //
    //     for (int i = 0; i < verts.Length; i++)
    //     {
    //         Vector3Int key = new Vector3Int(
    //             Mathf.RoundToInt(verts[i].x / weldTolerance),
    //             Mathf.RoundToInt(verts[i].y / weldTolerance),
    //             Mathf.RoundToInt(verts[i].z / weldTolerance)
    //         );
    //
    //         if (!positionMap.ContainsKey(key))
    //             positionMap[key] = new List<int>();
    //
    //         positionMap[key].Add(i);
    //     }
    //
    //     foreach (var group in positionMap.Values)
    //     {
    //         foreach (int index in group)
    //         {
    //             linkedVerticesByIndex[index] = group;
    //         }
    //     }
    // }
    //
    // private void FindVerticesByMaterial(Material targetMaterial, out List<int> vertexIndices)
    // {
    //     vertexIndices = new List<int>();
    //
    //     Renderer renderer = meshFilter.GetComponent<Renderer>();
    //
    //     if (renderer == null)
    //     {
    //         Debug.LogError("No hay Renderer en este objeto.");
    //         return;
    //     }
    //
    //     HashSet<int> vertexSet = new HashSet<int>();
    //
    //     for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
    //     {
    //         Material mat = renderer.sharedMaterials[subMesh];
    //
    //         if (mat == null)
    //             continue;
    //
    //         if (mat.name.Replace(" (Instance)", "") !=
    //             targetMaterial.name.Replace(" (Instance)", ""))
    //             continue;
    //
    //         int[] triangles = mesh.GetTriangles(subMesh);
    //
    //         foreach (int index in triangles)
    //         {
    //             vertexSet.Add(index);
    //         }
    //
    //         Debug.Log($"Material encontrado en SubMesh {subMesh}: {mat.name}");
    //     }
    //
    //     vertexIndices = vertexSet.ToList();
    //
    //     Debug.Log($"Vértices filtrados: {vertexIndices.Count}");
    // }
    //
    //
    // [Button]
    // public void MoveDynamicVerticesRange(int index0, int index1, Vector3 increment)
    // {
    //     allVertices = mesh.vertices;
    //
    //     index0 = Mathf.Clamp(index0, 0, dynamicVertexIndices.Count);
    //     index1 = Mathf.Clamp(index1, 0, dynamicVertexIndices.Count);
    //
    //     HashSet<int> moved = new HashSet<int>();
    //
    //     for (int i = index0; i < index1; i++)
    //     {
    //         int realIndex = dynamicVertexIndices[i];
    //
    //         if (!linkedVerticesByIndex.TryGetValue(realIndex, out List<int> linkedVertices))
    //             continue;
    //
    //         foreach (int linkedIndex in linkedVertices)
    //         {
    //             if (moved.Add(linkedIndex))
    //                 allVertices[linkedIndex] += increment;
    //         }
    //     }
    //
    //     mesh.vertices = allVertices;
    //     mesh.RecalculateNormals();
    //     mesh.RecalculateBounds();
    // }
    //
    // private void Update2()
    // {
    //     ApplyForceToSutureVertices();
    //     
    //     AddForceToVertexGroup();
    //     
    //     SimulateMassSpringDamper();
    //
    //
    //     if (Input.GetMouseButton(0))
    //         selectedVertices[0]++;
    //     
    // }
    //

    //
    // private void ApplyForceToSutureVertices()
    // {
    //     foreach (int index in sutureVertexIndices)
    //     {
    //         Vector3 acceleration = sutureForce / mass;
    //         velocities[index] += acceleration * Time.deltaTime;
    //         allVertices[index] += velocities[index] * Time.deltaTime;
    //         
    //         
    //         // if (!linkedVerticesByIndex.TryGetValue(index, out List<int> linkedVertices))
    //         //     continue;
    //         // HashSet<int> affected = new HashSet<int>();
    //         //
    //         // foreach (int realIndex in linkedVertices)
    //         // {
    //         //     if (affected.Add(realIndex))
    //         //     {
    //         //         Vector3 acceleration = sutureForce / mass;
    //         //         velocities[realIndex] += acceleration * Time.deltaTime;
    //         //         allVertices[realIndex] += velocities[realIndex] * Time.deltaTime;
    //         //     }
    //         // }
    //     }
    // }
    //
    //
    // private void SimulateMassSpringDamper()
    // {
    //     allVertices = mesh.vertices;
    //
    //     float dt = Time.deltaTime;
    //
    //     HashSet<int> moved = new HashSet<int>();
    //
    //     foreach (int index in dynamicVertexIndices)
    //     {
    //         if (!linkedVerticesByIndex.TryGetValue(index, out List<int> linkedVertices))
    //             continue;
    //
    //         foreach (int realIndex in linkedVertices)
    //         {
    //             if (!moved.Add(realIndex))
    //                 continue;
    //
    //             Vector3 displacement = allVertices[realIndex] - originalVertices[realIndex];
    //
    //             Vector3 springForce = -stiffness * displacement;
    //             Vector3 dampingForce = -damping * velocities[realIndex];
    //
    //             Vector3 force = springForce + dampingForce;
    //
    //             Vector3 acceleration = force / mass;
    //
    //             velocities[realIndex] += acceleration * dt;
    //             allVertices[realIndex] += velocities[realIndex] * dt;
    //         }
    //     }
    //
    //     mesh.vertices = allVertices;
    //     mesh.RecalculateNormals();
    //     mesh.RecalculateBounds();
    // }
    //
    // [Button]
    // public void AddForceToDynamicVerticesRange(int index0, int index1, Vector3 force)
    // {
    //     index0 = Mathf.Clamp(index0, 0, dynamicVertexIndices.Count);
    //     index1 = Mathf.Clamp(index1, 0, dynamicVertexIndices.Count);
    //
    //     HashSet<int> affected = new HashSet<int>();
    //
    //     for (int i = index0; i < index1; i++)
    //     {
    //         int index = dynamicVertexIndices[i];
    //
    //         if (!linkedVerticesByIndex.TryGetValue(index, out List<int> linkedVertices))
    //             continue;
    //
    //         foreach (int realIndex in linkedVertices)
    //         {
    //             if (affected.Add(realIndex))
    //             {
    //                 velocities[realIndex] += force / mass;
    //             }
    //         }
    //     }
    // }