using System;
using Sirenix.OdinInspector;
using UnityEngine;

public class SuturePoint : MonoBehaviour
{
    public DeformableMesh deformedMesh;

    public int vertexIndex;
    public Vector3 offset;
    public bool used;


    private void OnValidate()
    {
        UpdateSuturePointPosition();
    }

    private void Update()
    {
        UpdateSuturePointPosition();
    }

    [Button]
    private void UpdateSuturePointPosition()
    {
        // ---- MODIFICADO RESPECTO A SuturingSimulator ----
        // Dos problemas del original:
        //  1) usaba meshFilter.mesh, que en modo edición INSTANCIA la malla y la
        //     filtra ("This will leak meshes"). En edición se usa sharedMesh.
        //  2) llamaba a .vertices TRES veces por punto y por frame; cada llamada
        //     copia el array entero (4735 vértices x 11 puntos x 3 = ~156k copias
        //     de Vector3 por frame). Ahora se pide una sola vez.
        if (deformedMesh == null || deformedMesh.meshFilter == null) return;

        var mesh = Application.isPlaying
            ? deformedMesh.meshFilter.mesh
            : deformedMesh.meshFilter.sharedMesh;
        if (mesh == null) return;

        var vertices = mesh.vertices;
        if (vertexIndex < 0 || vertexIndex >= vertices.Length) return;

        transform.position = vertices[vertexIndex] + offset;
    }
}
