# SQL antes/después

- Fixtures controlados: 5.
- Gmail/cursor sin cambios: True.
- Fixtures eliminados al finalizar.
- Migración 009 recuperada sobre estado parcial: exit code 0.
- Segunda ejecución de 009: exit code 0, sin objetos ni filas duplicados.
- Índice filtrado `UX_DocumentoGroundTruth_DocumentoVigente`: creado y operativo.
- Prueba en base temporal: THROW antes de COMMIT dejó 0 tablas, 0 keys, 0 FKs, 0 CHECKs y 0 índices.
- Ejecución original posterior en base temporal: exit code 0; segunda ejecución: exit code 0.
- Esquema final temporal: 13 columnas, 2 keys, 2 FKs, 6 CHECKs, 4 índices y 0 filas.
- Base temporal eliminada: True.
