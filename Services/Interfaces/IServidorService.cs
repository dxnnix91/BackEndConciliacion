using Backend.Models;

namespace Backend.Services.Interfaces;

/// <summary>
/// Resuelve la configuración de conexión (IP/puerto/base) de cada local, a partir de la
/// colección Mongo "connections" (ver ConexionesLocalesSettings). Se cachea en memoria porque
/// es una colección de configuración pequeña que casi no cambia, así que ya no hay que golpear
/// Mongo por cada local en cada conciliación.
/// </summary>
public interface IServidorService
{
    /// <summary>Devuelve todos los locales configurados en la colección "connections".</summary>
    Task<IReadOnlyList<ServerConfig>> ObtenerServidoresAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca la configuración de servidor a partir del código de local (ej. "K004").
    /// Devuelve null si no existe ningún documento con ese tiendaName.
    /// </summary>
    Task<ServerConfig?> BuscarPorLocalAsync(string local, CancellationToken cancellationToken = default);
}