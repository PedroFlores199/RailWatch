# Procedencia de las muestras

Capturas realizadas el 15 de noviembre de 2026 desde los endpoints públicos `https://gtfsrt.renfe.com/vehicle_positions.pb` y `https://gtfsrt.renfe.com/trip_updates.pb`. Se conservaron tres lecturas separadas unos 25 segundos. Cada par tiene su timestamp original en el encabezado GTFS-Realtime.

`schedule.zip` contiene estaciones y un subconjunto de rutas, viajes y paradas del horario de Cercanías publicado en `https://ssl.renfe.com/ftransit/Fichero_CER_FOMENTO/fomento_transit.zip`, suficiente para consultar nombres y relacionar los viajes que coinciden con la muestra. El modo Live descarga el horario completo; los trenes no relacionados con la muestra siguen mostrando su posición y el identificador de parada disponible.

Fuente y atribución: **Renfe Open Data**, licencia [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/). Los datos se han filtrado para reducir el archivo de horarios; las respuestas Protobuf se mantienen sin modificar. La reproducción conserva etiquetas y fechas para distinguirla de circulación actual.
