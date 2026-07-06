# Entrenador de Sutura Robótica (Quest 3)

Sistema de entrenamiento tipo consola da Vinci: los controladores del Quest actúan como mandos maestros que teleoperan dos brazos robóticos sobre un campo quirúrgico magnificado (escala 5x para precisión fina).

## Generar las escenas

1. Abre el proyecto en Unity 6000.4.4f1 y espera a que compile.
2. Menú **SutureTrainer → Construir todo (materiales + escenas)**.
   Esto crea los materiales, las 6 escenas (`Assets/SutureTrainer/Scenes/`) y las añade a Build Settings.
3. Abre `ST_00_Menu` y pulsa Play (con Quest Link) o compila para el visor.

## Configuración para Quest 3 (una sola vez)

1. **File → Build Profiles → Android** → Switch Platform.
2. **Project Settings → XR Plug-in Management → Android**: activa **OpenXR**.
3. En **OpenXR → Enabled Interaction Profiles**: añade *Oculus Touch Controller Profile*.
4. En OpenXR Feature Groups: activa **Meta Quest** / Meta XR Feature Group.
5. Conecta el Quest 3 (modo desarrollador activo) y **Build And Run**.

## Controles

| Acción | Control |
|---|---|
| Mover instrumento | Mover el controlador (escalado 0.75x, con filtro de temblor) |
| Rotar muñeca (EndoWrist) | Rotar el controlador |
| Cerrar mandíbulas / agarrar | Gatillo |
| Clutch (reposicionar mano sin mover instrumento) | Botón A (der.) / X (izq.) |
| Menús | Láser del controlador derecho + gatillo |

## Niveles

1. **Manejo de aguja** — tomar la aguja y alinearla con poses fantasma (3 repeticiones).
2. **Precisión en anillos** — pasar la punta por 4 anillos; mide desviación al centro.
3. **Punto simple** — 3 puntos entrada→salida sobre la herida, con paso de hilo.
4. **Sutura continua** — 5 pases con el mismo hilo, tensión uniforme.
5. **Nudo intracorpóreo** — punto + 3 lazadas (2+1+1) enrollando el hilo en el instrumento izquierdo.

## Métricas y puntuación

Por nivel: tiempo, recorrido total de las puntas (economía de movimiento), errores (punción fuera de diana, tensión excesiva, aguja caída) y precisión media en mm. 1–3 estrellas según umbrales configurables en cada componente de nivel (`parTimeSec`, `parPathMeters`, `maxErrors`).

## Arquitectura (Assets/SutureTrainer/Scripts)

- `MasterInput` / `RoboticArm` — teleoperación maestro-esclavo con clutch, escalado y pivote en trócar.
- `SutureNeedle`, `VerletThread`, `TissuePatch` — aguja curva procedural, hilo con física Verlet (pines, deslizamiento, tensión, cincha), tejido deformable.
- `TrainingLevel` + `Level1..5` — lógica de ejercicios.
- `MetricsRecorder`, `HUD`, `ResultsPanel`, `ControllerLaser`, `WorldButton` — métricas y UI.
- `Editor/SceneBuilder` — generación automática de materiales y escenas.

## Ajustes rápidos

- Sensibilidad: `RoboticArm.motionScale` (0.75 por defecto).
- Orientación neutra del instrumento: `RoboticArm.rotationOffsetEuler`.
- Dificultad: radios de `PunctureTarget`/`RingTarget`, tolerancias en cada nivel.
- Longitud del hilo: `VerletThread.totalLength`.
