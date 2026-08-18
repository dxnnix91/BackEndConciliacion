using Backend.Models;

namespace Backend.Services.Interfaces;

/// <summary>
/// Ejecuta, para un servidor SQL Server específico, la consulta SQL original (sección 6),
/// sin alterar sus JOINs ni nombres de tablas. El filtro ya no es por rango de fechas: por
/// decisión explícita (para agilizar la consulta), se filtra directamente por la lista de
/// codigo_app que ya vienen de Mongo para ese local, ya que esa es la llave real de
/// conciliación (externalReference de Mongo == codigo_app de SQL).
/// IMPORTANTE: con este cambio ya no se puede detectar FALTA_MONGO (pagos que existen en SQL
/// pero nunca llegaron a Mongo), porque solo se consultan los códigos que Mongo ya entregó.
/// Fue una decisión consciente a cambio de velocidad.
/// </summary>
public interface ISqlServerService
{
    /// <summary>
    /// Se conecta al servidor indicado (IP + base de datos) y devuelve, ya mapeadas a
    /// SqlPayment, únicamente las filas cuyo codigo_app esté en <paramref name="codigosApp"/>.
    /// Una sola conexión por servidor (sección 11): NO se realiza una consulta por transacción,
    /// se manda todo el lote de códigos de una vez en un solo IN (...).
    /// </summary>
    Task<List<SqlPayment>> ObtenerPagosPorCodigosAsync(ServerConfig servidor, IReadOnlyList<string> codigosApp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Se conecta al servidor indicado y devuelve, mapeadas a FacturaDomicilioSql, las facturas
    /// (Cabecera_Factura) cuyo cfac_id esté en <paramref name="invoiceIds"/> (los invoiceId de
    /// Mongo para pedidos a domicilio). A diferencia de ObtenerPagosPorCodigosAsync, NO filtra
    /// por forma de pago: trae la factura sin importar cómo quedó registrada, porque el sistema
    /// local a veces la asigna mal.
    /// </summary>
    Task<List<FacturaDomicilioSql>> ObtenerFacturasPorInvoiceIdsAsync(ServerConfig servidor, IReadOnlyList<string> invoiceIds, CancellationToken cancellationToken = default);

    /// <summary>Prueba de conexión simple, usada por GET /api/conciliacion/servidores.</summary>
    Task<bool> ProbarConexionAsync(ServerConfig servidor, CancellationToken cancellationToken = default);
}