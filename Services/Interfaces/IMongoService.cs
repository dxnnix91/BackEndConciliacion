using Backend.Models;

namespace Backend.Services.Interfaces;

/// <summary>Consulta MongoDB para obtener transacciones aprobadas de un rango de fechas (sección 3).</summary>
public interface IMongoService
{
    /// <summary>
    /// Obtiene las transacciones con status "approved" cuyo createdAt cae dentro de
    /// [inicio de fechaInicio, inicio del día siguiente a fechaFin). Cada transacción incluye
    /// el campo calculado Local, extraído de externalReference.
    /// </summary>
    Task<List<MongoPayment>> ObtenerTransaccionesAprobadasAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que ObtenerTransaccionesAprobadasAsync, pero filtrando por status "cancelled" en vez
    /// de "approved". Se usa para detectar pagos cancelados en Mongo que sí tienen una factura
    /// entregada en SQL Server (ver ConciliacionService.ProcesarLocalCanceladosAsync).
    /// </summary>
    Task<List<MongoPayment>> ObtenerTransaccionesCanceladasAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default);
}