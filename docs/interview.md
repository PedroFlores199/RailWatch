# Recorrido para una entrevista

1. Abre el mapa y explica por qué la banda indica Replay: son capturas de datos públicos, no una simulación presentada como directa.
2. Filtra C1 y selecciona un tren. Muestra su próxima parada y el retraso, diferenciando dato desconocido de cero.
3. Añade una regla por línea y umbral. Explica que el worker guarda la regla en PostgreSQL y evita repetir una alerta mientras el tren permanece en la misma situación.
4. Abre el cliente WPF. El ViewModel recibe contratos compartidos, se prueba sin WPF y la ventana usa binding e inyección de dependencias.
5. Explica los almacenes: histórico relacional, mensaje bruto auditable y estado efímero distribuido.
6. Muestra la consulta de percentiles y la de empeoramiento con `LAG`. Defiende Dapper por el control del SQL, sin exagerar la pequeña diferencia medida frente a EF Core.
7. Enseña una prueba de PostgreSQL con Testcontainers y la captura del smoke test de Windows en Actions.

Preguntas para preparar: ¿qué ocurre si no llega `delay`?, ¿cómo se representa una parada a las 25:30?, ¿qué sucede tras reiniciar la API?, ¿cómo cambiaría la arquitectura con varias réplicas del worker?, ¿qué significa realmente el porcentaje de puntualidad mostrado?
