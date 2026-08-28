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
        currentSuturePoint = _suturePoint;
        pinTransform = _suturePoint.transform;
        pinLocalPosition = Vector3.zero;
        PinNode();
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

    [Button]
    public bool SlidePin(int _index)
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
