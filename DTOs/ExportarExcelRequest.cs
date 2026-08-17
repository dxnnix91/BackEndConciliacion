using Backend.Models;

namespace Backend.DTOs;

/// <summary>
/// Cuerpo para POST /api/conciliacion/exportar (sección 26). El frontend envía el resumen y
/// el detalle que ya tiene en pantalla (resultado de la última ejecución) para generar el Excel,
/// evitando que el backend tenga que mantener en memoria el detalle completo de conciliaciones pasadas.
/// </summary>
public class ExportarExcelRequest
{
    public ConciliacionResumen? Resumen { get; set; }
    public List<ConciliacionResult> Resultados { get; set; } = new();
}