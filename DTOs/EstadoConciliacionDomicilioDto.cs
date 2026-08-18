using Backend.Models;

namespace Backend.DTOs;

/// <summary>Respuesta de GET /api/conciliacion-domicilio/estado.</summary>
public class EstadoConciliacionDomicilioDto
{
    public EstadoEjecucion Estado { get; set; }
    public string? FechaInicio { get; set; }
    public string? FechaFin { get; set; }

    public int LocalesTotal { get; set; }
    public int LocalesProcesados { get; set; }

    public int SolicitudesTotal { get; set; }
    public int SolicitudesProcesadas { get; set; }

    /// <summary>Locales que se están procesando en este momento (varios a la vez, en paralelo).</summary>
    public List<string> LocalesEnProceso { get; set; } = new();

    public DateTime? IniciadoEn { get; set; }
    public DateTime? FinalizadoEn { get; set; }

    public ConciliacionDomicilioResumen? Resumen { get; set; }
}