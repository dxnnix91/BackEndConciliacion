using Backend.Models;

namespace Backend.Services.Interfaces;

/// <summary>
/// Resuelve la configuración de conexión (IP + BASE) de cada local. Ya no lee un CSV:
/// la conexión se genera dinámicamente a partir del número de local, usando los patrones
/// definidos en la sección "Locales" de appsettings.
/// </summary>
public interface IServidorService
{
    /// <summary>Devuelve todos los locales configurados como válidos (sección "Locales:Codigos").</summary>
    IReadOnlyList<ServerConfig> ObtenerServidores();

    /// <summary>
    /// Genera/busca la configuración de servidor a partir del código de local (ej. "K004").
    /// Devuelve null si no se puede interpretar el número, o si hay una lista de códigos
    /// válidos configurada y el número no pertenece a ella.
    /// </summary>
    ServerConfig? BuscarPorLocal(string local);
}