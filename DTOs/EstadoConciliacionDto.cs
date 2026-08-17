using Backend.Models;

namespace Backend.DTOs;

/// <summary>Respuesta de GET /api/conciliacion/estado (secciones 19 y 20).</summary>
public class EstadoConciliacionDto
{
    public EstadoEjecucion Estado { get; set; }
    public string? FechaInicio { get; set; }
    public string? FechaFin { get; set; }

    public int LocalesTotal { get; set; }
    public int LocalesProcesados { get; set; }

    public int TransaccionesTotal { get; set; }
    public int TransaccionesProcesadas { get; set; }

    /// <summary>
    /// Locales que se están procesando en este momento (varios a la vez, ya que ahora cada
    /// local se procesa en paralelo e independiente de los demás).
    /// </summary>
    public List<string> LocalesEnProceso { get; set; } = new();

    public DateTime? IniciadoEn { get; set; }
    public DateTime? FinalizadoEn { get; set; }

    public ConciliacionResumen? Resumen { get; set; }
}