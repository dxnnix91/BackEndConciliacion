using Backend.DTOs;

namespace Backend.Services.Interfaces;

/// <summary>Orquesta el flujo de conciliación de pedidos a domicilio.</summary>
public interface IConciliacionDomicilioService
{
    /// <summary>
    /// Ejecuta la conciliación de domicilios para el rango de fechas indicado (inclusive en
    /// ambos extremos). Lanza InvalidOperationException si ya hay una en curso.
    /// </summary>
    Task<ConciliacionDomicilioResponse> EjecutarAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default);

    EstadoConciliacionDomicilioDto ObtenerEstado();
}