namespace Backend.Services.Interfaces;

/// <summary>Consulta la base central de Azure (MAXPOINT) que tiene todas las tiendas.</summary>
public interface IRestauranteCentralService
{
    /// <summary>
    /// Devuelve el rst_cod_tienda (ej. "K172") correspondiente a un rst_id (branchOffice),
    /// o null si no se encuentra o si la consulta falla.
    /// </summary>
    Task<string?> ObtenerCodigoTiendaPorRstIdAsync(int rstId, CancellationToken cancellationToken = default);
}