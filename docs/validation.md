# Validación y límites

Validado en macOS ARM64 con .NET 10.0.404 y Docker Desktop. Compilan los proyectos web, worker y WPF con Windows targeting. Las 20 pruebas pasan con `RUN_CONTAINER_TESTS=1`, incluida PostgreSQL real. WPF requiere Windows para ejecutar su interfaz; GitHub Actions contiene una prueba que abre la ventana conectada a la API y renderiza una captura verificable.

Los frames de ejemplo son respuestas públicas de Renfe, no datos sintéticos de rendimiento. El benchmark usa otra base y etiqueta su fuente `SyntheticBenchmark`. No se mezclan con estadísticas Live/Replay. El informe de puntualidad se calcula sobre las capturas del repositorio y conserva esa limitación.

El feed no garantiza cobertura de AVE/Media Distancia. No se estiman trayectorias entre muestras. Un modelo de predicción requiere más histórico que el incluido y no conserva su estado entre reinicios. Los paquetes MSIX se generan sin certificado de firma; los ZIP contienen el ejecutable autocontenido.

Las reglas pueden configurarse libremente en esta demostración local. La API pública necesitaría autenticación, límites de acceso, migraciones de esquema y secretos externos. No se afirma una auditoría independiente ni la ausencia absoluta de errores.
