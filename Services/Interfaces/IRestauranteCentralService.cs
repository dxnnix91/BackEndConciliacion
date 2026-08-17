namespace Backend.Services.Interfaces;

/// <summary>
/// Resuelve el código de tienda (tiendaName) a partir del restauranteId, usando la misma
/// colección Mongo "connections" que IServidorService (ya no se consulta Azure SQL/MAXPOINT).
/// Se usa como respaldo en MongoService cuando el externalReference no trae un local válido,
/// pero sí viene metadataCreatePayment.branchOffice (que corresponde al restauranteId).
/// </summary>
public interface IRestauranteCentralService
{
    /// <summary>
    /// Devuelve el tiendaName (ej. "K172") correspondiente a un restauranteId (branchOffice),
    /// o null si no se encuentra ningún documento con ese restauranteId.
    /// </summary>
    Task<string?> ObtenerCodigoTiendaPorRstIdAsync(int rstId, CancellationToken cancellationToken = default);
}