using System.Collections.Generic;
using AdvancedRope;
using Sirenix.OdinInspector;
using UnityEngine;

public class BoyRopePinHole : MonoBehaviour
{
    public VerletRopeGenerator verletRopeGenerator;
    public SutureManager sutureManager;
    public bool pinEnd;

    public int pinIndex;
    public Transform pinTransform;
    public Vector3 pinLocalPosition;
    public SuturePoint currentSuturePoint;
    
    public float pinLength = 0f;
    public float slideThreshold = 0.1f;
    
    [ListDrawerSettings(ShowIndexLabels = true)]
    public bool[] pinnedNodes;

    public bool printDebug = false;
    
    void Start()
    {
        
    }

    
    void Update()
    {
        if(pinnedNodes.Length != verletRopeGenerator.Nodes.Count)
            pinnedNodes = new bool[verletRopeGenerator.Nodes.Count];
        
        for (int i = 1; i < verletRopeGenerator.Nodes.Count; i++)
        {
            if(verletRopeGenerator.Nodes[i].isPinned)
            {
                pinLength = (verletRopeGenerator.Nodes[i].pinnedTransform.position -
                             verletRopeGenerator.Nodes[i - 1].position).magnitude;
                //print($"i:{i}, pinLength:{pinLength}");
                if (pinLength > slideThreshold)
                    SlidePin(i);
            }
            pinnedNodes[i] = verletRopeGenerator.Nodes[i].isPinned;
        }
    }

    [Button]
    public void PinNode()
    {
        if(printDebug) print($"<color=green>PinNode {pinIndex}</color>");
        if (!verletRopeGenerator.Nodes[pinIndex].isPinned)
        {
            verletRopeGenerator.PinNode(pinIndex, pinTransform, pinLocalPosition);
            if(currentSuturePoint != null)
                currentSuturePoint.used = true;
        }
    }
    
    [Button]
    public void PinNode(int _index, Transform _transform, Vector3 _localPosition)
    {
        pinIndex = _index;
        pinTransform = _transform;
        pinLocalPosition = _localPosition;
        currentSuturePoint = null;
        PinNode();
    }
    
    [Button]
    public void PinNode(SuturePoint _suturePoint)
    {
        pinIndex = 1;
        // ---- MODIFICADO ----
        // Si el pin del punto anterior sigue en el nodo 1 (aún no se ha tirado
        // del hilo lo bastante para que deslice), PinNode() lo ignoraba en
        // silencio y el hilo no pasaba por este punto. Se desliza primero.
        HacerSitio(_suturePoint.transform.position);
        pinIndex = 1;   // SlidePin -> PinNode(int,...) reescribe pinIndex: restaurarlo
        if (verletRopeGenerator.Nodes.Count > 1 && verletRopeGenerator.Nodes[1].isPinned)
            Debug.LogWarning($"[Hilo] No hay nodo libre para {_suturePoint.name}: el hilo no pasa por él.");
        currentSuturePoint = _suturePoint;
        pinTransform = _suturePoint.transform;
        pinLocalPosition = Vector3.zero;
        PinNode();
    }
    /// <summary>
    /// ---- AÑADIDO ----
    /// Antes de anclar un punto nuevo en el nodo 1, desliza los anclajes
    /// existentes hacia la cola hasta que entre el nodo 1 y el anclaje más
    /// cercano a la aguja quede tanto hilo como distancia hay entre los dos
    /// puntos. Sin esto, puntos a 30-40 mm quedaban unidos por UN segmento de
    /// 5 mm (estirado x19) y la sutura se veía como una línea recta tensa.
    /// </summary>
    void HacerSitio(Vector3 nuevoPunto)
    {
        var nodes = verletRopeGenerator.Nodes;
        float seg = Mathf.Max(1e-4f, verletRopeGenerator.SegmentLength);

        for (int intento = 0; intento < nodes.Count; intento++)
        {
            int primero = -1;
            for (int i = 1; i < nodes.Count; i++)
                if (nodes[i].isPinned) { primero = i; break; }
            if (primero < 0) return;                       // no hay anclajes: sitio de sobra

            Vector3 anterior = nodes[primero].pinnedTransform != null
                ? nodes[primero].pinnedTransform.position
                : nodes[primero].position;
            int necesarios = Mathf.CeilToInt(Vector3.Distance(nuevoPunto, anterior) / seg) + 1;
            if (primero >= 1 + necesarios) return;        // ya hay hilo suficiente

            if (!SlidePin(primero)) return;               // el hilo se acabó: no se puede más
        }
    }

    [Button]
    public void UnpinNode(int _index)
    {
        if(printDebug) print($"<color=orange>UnpinNode {_index}</color>");
        if(_index >= verletRopeGenerator.Nodes.Count - 1 && pinEnd)
        {
            return;
        }     
        
        verletRopeGenerator.UnpinNode(_index);
    }

    /// <summary>
    /// ---- MODIFICADO ----
    /// Al tirar de la aguja el hilo corre por TODOS los agujeros a la vez: cada
    /// anclaje desde _index hacia la cola avanza un nodo, conservando el hilo
    /// que hay entre punto y punto. El original solo empujaba un anclaje
    /// cuando chocaba con el siguiente, así que los anclajes se apelotonaban
    /// en nodos contiguos y los tramos entre puntos se estiraban ~4.5x.
    /// </summary>
    [Button]
    public bool SlidePin(int _index)
    {
        var nodes = verletRopeGenerator.Nodes;
        if (_index <= 0 || _index >= nodes.Count || !nodes[_index].isPinned) return true;

        int ultimo = nodes.Count - 1;
        float seg = Mathf.Max(1e-4f, verletRopeGenerator.SegmentLength);

        // anclajes desde _index hacia la cola, en orden
        var lista = new List<int>();
        for (int j = _index; j < nodes.Count; j++)
            if (nodes[j].isPinned) lista.Add(j);

        // ¿cuántos arrastra? el siguiente solo si entre los dos ya no queda
        // hilo para la distancia que separa sus agujeros (el tramo está tenso);
        // si hay holgura, el punto anterior aguanta por rozamiento.
        int n = 1;
        for (int k = 1; k < lista.Count; k++)
        {
            int nodosEntre = lista[k] - (lista[k - 1] + 1);
            float d = Vector3.Distance(Agujero(lista[k - 1]), Agujero(lista[k]));
            if (nodosEntre * seg < d) n++; else break;
        }

        if (lista[n - 1] >= ultimo)
        {
            if (pinEnd)
            {
                if(printDebug) print($"<color=magenta>PINED END TightenSuture</color>");
                sutureManager.TightenSuture();
                return false;
            }
            UnpinNode(ultimo);   // se sale el extremo del hilo
            n--;
        }

        for (int k = n - 1; k >= 0; k--)   // de la cola hacia la aguja
        {
            int j = lista[k];
            var t = nodes[j].pinnedTransform;
            var localT = nodes[j].pinLocalPos;
            verletRopeGenerator.UnpinNode(j);
            verletRopeGenerator.PinNode(j + 1, t, localT);
        }
        if(printDebug) print($"<color=magenta>SlidePin desde {_index} ({n} anclajes)</color>");
        return true;
    }

    Vector3 Agujero(int i)
    {
        var nd = verletRopeGenerator.Nodes[i];
        return nd.pinnedTransform != null ? nd.pinnedTransform.TransformPoint(nd.pinLocalPos) : nd.position;
    }

    /// <summary>Algoritmo original de SuturingSimulator (se conserva como referencia).</summary>
    public bool SlidePinOriginal(int _index)
    {
        if (!verletRopeGenerator.Nodes[_index].isPinned)// nodo libre
        {
            return true;
        }
        
        bool doSlide = false;
        // nodo SI anclado
        {
            if (_index >= verletRopeGenerator.Nodes.Count - 1) // ultimo nodo 
            {
                if (pinEnd)// ultimo nodo anclado
                {
                    if(printDebug) print($"<color=magenta>PINED END TightenSuture</color>");
                    sutureManager.TightenSuture();
                    return false;
                }
                UnpinNode(_index);
                return true;
            }
            else // NO ultimo nodo
            {
                if (!verletRopeGenerator.Nodes[_index + 1].isPinned) // siguiente nodo libre
                {
                    doSlide = true;
                }
                else // siguiente nodo anclado
                {
                    doSlide = SlidePin(_index + 1);
                }
            }
        }

        if (doSlide)// si se puede deslizar
        {
            if(printDebug) print($"<color=magenta>SlidePin index {_index}</color>");
            var t = verletRopeGenerator.Nodes[_index].pinnedTransform;
            var localT = verletRopeGenerator.Nodes[_index].pinLocalPos;

            UnpinNode(_index);
            PinNode(_index + 1, t, localT);
            return true;
        }
        //  no se puede deslizar
        return false;
    }
    
    
    [Button]
    public void UnpinAll()
    {
        for (int i = 1; i < verletRopeGenerator.Nodes.Count; i++)
        {
            UnpinNode(i);
        }
    }
}
