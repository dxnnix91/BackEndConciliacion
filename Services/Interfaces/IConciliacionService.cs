using Backend.DTOs;

namespace Backend.Services.Interfaces;

/// <summary>Orquesta el flujo completo de conciliación descrito en la sección 10.</summary>
public interface IConciliacionService
{
    /// <summary>
    /// Ejecuta la conciliación para el rango de fechas indicado (inclusive en ambos extremos).
    /// Lanza InvalidOperationException si ya hay una conciliación en curso (sección 19).
    /// </summary>
    Task<ConciliacionResponse> EjecutarAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default);

    EstadoConciliacionDto ObtenerEstado();

    IReadOnlyList<ServidorDto> ObtenerServidores();
}