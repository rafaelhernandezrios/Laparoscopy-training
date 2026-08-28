using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Conecta el cierre de mandíbulas del <see cref="RoboticArm"/> con el
    /// blendshape de la pinza importada de SuturingSimulator (allí lo movía
    /// PinzaManager leyendo el botón del Touch; aquí la fuente es el brazo, que
    /// ya unifica Quest y Touch).
    ///
    /// El modelo trae DOS mallas: "Body" (sin blendshapes) y "Clamp" (con el
    /// canal "Closed"). Por eso se busca el renderer que de verdad tenga
    /// blendshapes en vez de tomar el primero que aparezca.
    ///
    /// Si no encuentra ninguno, no hace nada y las mandíbulas procedurales
    /// siguen funcionando.
    /// </summary>
    public class InstrumentVisual : MonoBehaviour
    {
        public const string CloseShapeName = "Closed";

        public RoboticArm arm;
        [Tooltip("Renderer con el blendshape de cierre. Se autodetecta si se deja vacío.")]
        public SkinnedMeshRenderer skinned;
        [Tooltip("Índice del blendshape de cierre. Se autodetecta por nombre.")]
        public int blendShapeIndex = -1;
        [Tooltip("Suavizado del cierre. 0 = instantáneo.")]
        [Range(0f, 30f)] public float closeSpeed = 12f;

        float _weight;

        void Reset() { Bind(); }
        void Awake() { Bind(); }

        void Bind()
        {
            if (arm == null) arm = GetComponentInParent<RoboticArm>();

            if (skinned == null || skinned.sharedMesh == null ||
                skinned.sharedMesh.blendShapeCount == 0)
            {
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                    {
                        skinned = smr;
                        break;
                    }
                }
            }

            if (skinned == null || skinned.sharedMesh == null) return;

            var mesh = skinned.sharedMesh;
            if (blendShapeIndex >= 0 && blendShapeIndex < mesh.blendShapeCount) return;

            int byName = mesh.GetBlendShapeIndex(CloseShapeName);
            blendShapeIndex = byName >= 0 ? byName : (mesh.blendShapeCount > 0 ? 0 : -1);
        }

        void LateUpdate()
        {
            if (arm == null || skinned == null || skinned.sharedMesh == null) return;
            if (blendShapeIndex < 0 || blendShapeIndex >= skinned.sharedMesh.blendShapeCount) return;

            float target = Mathf.Clamp01(arm.JawCloseness);
            _weight = closeSpeed <= 0f
                ? target
                : Mathf.Lerp(_weight, target, 1f - Mathf.Exp(-closeSpeed * Time.deltaTime));

            skinned.SetBlendShapeWeight(blendShapeIndex, _weight * 100f);
        }
    }
}
