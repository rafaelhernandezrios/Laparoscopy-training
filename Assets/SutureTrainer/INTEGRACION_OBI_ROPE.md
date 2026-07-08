# Integración futura de Obi Rope

Los niveles solo dependen de la clase abstracta `ThreadBase` y crean el hilo
mediante `TrainingLevel.ThreadFactory`. Para sustituir la simulación Verlet
por Obi Rope:

1. Compra e importa **Obi Rope** (Virtual Method, Asset Store).
2. Crea `ObiRopeThread : ThreadBase` implementando los miembros abstractos:
   - `Tension` → estiramiento medio de la cuerda (`rope.CalculateLength() / restLength`).
   - `PinThroughStitch(exit, entry)` → dos `ObiParticleAttachment` estáticos en esos puntos.
   - `CinchTo(point)` → mover los attachments del último par a `point`.
   - `RemainingTailSegments` → partículas después del último attachment.
   - `TailParticlePos` / `ParticleAt(t)` → posición de partículas del solver.
   - `anchor` → attachment dinámico a la cola de la aguja; `tailHolder` → attachment dinámico a la pinza.
3. Registra la fábrica antes de que cargue cualquier nivel:

```csharp
using UnityEngine;

namespace SutureTrainer
{
    public static class ObiBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            TrainingLevel.ThreadFactory = go => go.AddComponent<ObiRopeThread>();
        }
    }
}
```

Nada más: los cinco niveles usarán la nueva implementación sin cambios.
El mismo patrón sirve para el Geomagic Touch con `MasterInput` (crear un
`GeomagicInput` que rellene WorldPos/WorldRot/Trigger desde OpenHaptics).
