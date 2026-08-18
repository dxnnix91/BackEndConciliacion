using Backend.Models;

namespace Backend.Services.Interfaces;

/// <summary>Consulta MongoDB para obtener solicitudes de pago a domicilio de un rango de fechas.</summary>
public interface IMongoDomicilioService
{
    /// <summary>
    /// Obtiene TODAS las solicitudes (sin filtrar por status, a diferencia del flujo de kiosko)
    /// cuyo createdAt cae dentro de [inicio de fechaInicio, inicio del día siguiente a
    /// fechaFin). Cada solicitud incluye el campo calculado Local, extraído de requestId (con
    /// respaldo vía store.id contra la colección "connections").
    /// </summary>
    Task<List<RequestPaymentDocument>> ObtenerSolicitudesAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default);
}