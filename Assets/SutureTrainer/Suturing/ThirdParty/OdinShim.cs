// Shim de Sirenix.OdinInspector.
//
// El subsistema de sutura portado desde SuturingSimulator usa unos pocos
// atributos de Odin, que allí es un asset de pago. Ninguna clase hereda de
// SerializedMonoBehaviour, así que esos atributos son PURAMENTE COSMÉTICOS:
// solo dibujan botones y agrupaciones en el inspector.
//
// Declarándolos aquí como atributos vacíos, los scripts portados compilan sin
// tocar una sola línea, lo que permite volver a sincronizarlos con el proyecto
// original con un simple copiar y pegar.
//
// Si algún día se importa Odin de verdad en este proyecto, define ODIN_INSPECTOR
// (Odin lo hace por sí solo) y este archivo se desactiva sin conflicto.

#if !ODIN_INSPECTOR

using System;

namespace Sirenix.OdinInspector
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field,
                    AllowMultiple = true, Inherited = true)]
    public sealed class ButtonAttribute : Attribute
    {
        public ButtonAttribute() { }
        public ButtonAttribute(string name) { Name = name; }
        public string Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public sealed class FoldoutGroupAttribute : Attribute
    {
        public FoldoutGroupAttribute(string groupName) { GroupName = groupName; }
        public string GroupName { get; set; }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public sealed class GUIColorAttribute : Attribute
    {
        public GUIColorAttribute(string color) { Color = color; }
        public GUIColorAttribute(float r, float g, float b, float a = 1f) { }
        public string Color { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property,
                    AllowMultiple = false, Inherited = true)]
    public sealed class ListDrawerSettingsAttribute : Attribute
    {
        public bool ShowIndexLabels { get; set; }
        public bool DraggableItems { get; set; }
        public bool IsReadOnly { get; set; }
        public bool ShowItemCount { get; set; }
        public bool Expanded { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method,
                    AllowMultiple = false, Inherited = true)]
    public sealed class ShowInInspectorAttribute : Attribute
    {
    }
}

#endif
