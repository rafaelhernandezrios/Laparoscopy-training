using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class SutureManager : MonoBehaviour
{
    public DeformableMesh deformedMesh;
    public BoyRopePinHole boyRopePinHole;
    public float stiffnessSuture = 50f;
    public List<Vector3> sutureVertices;
    
    public GameObject proxy;
    public int neighborCount = 0;

    public float thightness = 0.01f;
    
    public int sutureTightnessVertexAmount = 1;
    
    public int lastIndex; 
    public int reachIndex;
    
    void Start()
    {
        
    }
    
    void Update()
    {
        // ---- MODIFICADO RESPECTO A SuturingSimulator ----
        // El original usaba UnityEngine.Input (Input Manager antiguo). Este
        // proyecto tiene el Input System nuevo como ÚNICO manejador activo, así
        // que esa llamada lanzaba InvalidOperationException CADA FRAME y abortaba
        // Update() entero: el tejido no reaccionaba a nada.
        // El atajo de teclado se conserva, pero leído por el Input System.
        //
        // Y con el Touch es la ÚNICA vía. SutureRig tensa con
        // MasterInput.Secondary, que en modo Touch está siempre en false:
        // ApplyWorldPose lo fija así porque el stylus solo tiene dos botones y
        // los dos están cogidos (delantero = mandíbulas, trasero = embrague).
        // Con el mando de Quest sigue valiendo B/Y.
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            TightenSuture();
    }
    
        
    [Button]
    public void SutureVertices()
    {
        sutureVertices.Add(proxy.transform.position);
        deformedMesh.SutureVertices(proxy, sutureVertices.Count-1);
    }
    
    [Button]
    public void SutureVertices(int _index)
    {
        sutureVertices.Add(proxy.transform.position);
        deformedMesh.SutureVertices(_index, sutureVertices.Count-1, neighborCount);
    }
    
    [Button]
    public void SutureVertices(SuturePoint suturePoint)
    {
        // Sin objetivo explícito se conserva el comportamiento original:
        // clavar el punto donde ya está.
        SutureVertices(suturePoint, suturePoint.transform.position);
    }

    /// <summary>
    /// Sutura un punto tirando de él hacia <paramref name="target"/>.
    ///
    /// El original guardaba SIEMPRE suturePoint.transform.position, o sea que
    /// suturar dejaba el labio clavado exactamente donde ya estaba y la herida
    /// no se aproximaba nada: solo la movía después TightenSuture(), y en la
    /// dirección de los puntos vecinos de la LISTA, no del labio de enfrente.
    /// Quien decide el objetivo es OrganRig, que es el único que sabe qué
    /// punto es pareja de cuál.
    /// </summary>
    public void SutureVertices(SuturePoint suturePoint, Vector3 target)
    {
        sutureVertices.Add(target);

        // ---- MODIFICADO RESPECTO A SuturingSimulator ----
        // SuturePoint.vertexIndex es un índice de la malla CRUDA (así lo usa el
        // propio SuturePoint: mesh.vertices[vertexIndex]). Pero
        // DeformableMesh.SutureVertices indexa en masterVertices, que es la
        // lista SOLDADA por weldTolerance: numeración distinta.
        //
        // Pasarlo sin traducir fija vértices arbitrarios del hígado, y al
        // tensar se deforma por donde no toca en vez de cerrar la herida.
        // vertexToMaster es justo el mapa crudo -> soldado.
        int masterIndex = suturePoint.vertexIndex;
        if (deformedMesh.vertexToMaster != null &&
            deformedMesh.vertexToMaster.TryGetValue(suturePoint.vertexIndex, out int mapped))
            masterIndex = mapped;

        deformedMesh.SutureVertices(masterIndex, sutureVertices.Count - 1, neighborCount);
    }

    [Button]
    public void TightenSuture()
    {
        // ---- MODIFICADO RESPECTO A SuturingSimulator ----
        // Con el órgano de este proyecto el tensado aproxima cada punto a su
        // pareja del labio de enfrente (OrganRig). El algoritmo original de
        // abajo empuja hacia los vecinos de la LISTA, a lo largo de la incisión,
        // y abría la herida; se conserva solo para escenas sin OrganRig.
        if (SutureTrainer.OrganRig.I != null)
        {
            SutureTrainer.OrganRig.I.ApproximatePairs(thightness);
            return;
        }

        if( sutureTightnessVertexAmount < 1)
            return;
        lastIndex = sutureVertices.Count - 1;
        reachIndex = lastIndex - sutureTightnessVertexAmount;
        reachIndex = Mathf.Max(reachIndex, 0);
        
        var copySutureVertices = new List<Vector3>(sutureVertices);
        
        for (int i = lastIndex; i >= reachIndex; i--)
        {
            var dir = Vector3.zero;
            if (i == 0 || i == lastIndex)
            {
                //dir = (copySutureVertices[i + 1] - copySutureVertices[i]);
            }
            else
            {
                dir = (copySutureVertices[i - 1] - copySutureVertices[i]);
                if(i + 1 <= lastIndex)
                    dir +=  (copySutureVertices[i + 1] - copySutureVertices[i]);
            }
            
            //print($"copySutureVertices[{i}] {copySutureVertices[i]} dir {dir}");
            copySutureVertices[i] +=  dir.normalized* thightness;
            //print($"copySutureVertices[{i}] {copySutureVertices[i]} ...");
        }
        
        sutureVertices = copySutureVertices;
    }
    
    [Button]
    public void ResetSuturingPoints()
    {
        sutureVertices.Clear();
        if (boyRopePinHole != null) boyRopePinHole.UnpinAll();   // en los niveles no hay hilo Verlet
        if (SutureTrainer.OrganRig.I != null) SutureTrainer.OrganRig.I.ClearSutures();

        deformedMesh.ResetSuturingPoints();
        
        var suturePoints = FindObjectsOfType<SuturePoint>();
        for (int i = 0; i < suturePoints.Length; i++)
        {
            suturePoints[i].used = false;
        }
    }
}
