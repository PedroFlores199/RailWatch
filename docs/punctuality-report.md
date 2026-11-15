# Informe de puntualidad de la muestra

Fuente: Renfe Open Data, capturas públicas del 15 de noviembre de 2026. Modo: **Replay**. El análisis comprende las tres lecturas incluidas en el repositorio, no toda la jornada de servicio.

**890 observaciones con retraso conocido**, en 25 líneas; **78.3%** dentro de cinco minutos. Los valores desconocidos quedan excluidos. Repetir los frames no multiplica las observaciones gracias al índice único por tren, fecha y modo.

| Línea | Observaciones | Puntualidad ≤5 min | Retraso medio | P95 |
| --- | ---: | ---: | ---: | ---: |
| R2N | 33 | 45.5% | 13.3 min | 48.4 min |
| R11 | 24 | 50.0% | 12.1 min | 50.0 min |
| C9 | 6 | 50.0% | 13.8 min | 28.0 min |
| R16 | 7 | 57.1% | 9.4 min | 22.0 min |
| R4 | 48 | 62.5% | 11.1 min | 57.5 min |
| R1 | 42 | 64.3% | 13.6 min | 75.2 min |
| C6 | 27 | 66.7% | 6.7 min | 23.7 min |
| R15 | 9 | 66.7% | 13.0 min | 34.0 min |
| R8 | 12 | 75.0% | 8.2 min | 29.0 min |
| C4 | 21 | 76.2% | 3.6 min | 10.0 min |

No debe interpretarse como una clasificación de puntualidad de Renfe: es una muestra breve, ponderada por observaciones, y no mide viajes completos ni controla la representatividad por núcleo ferroviario. No se extrapola al día, a la semana ni al servicio actual.

Se puede reproducir arrancando Docker Compose en Replay y consultando `/api/statistics/lines?mode=Replay`. Los datos originales y su atribución figuran en `samples/README.md`.
