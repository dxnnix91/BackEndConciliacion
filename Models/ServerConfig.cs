namespace Backend.Models;

/// <summary>
/// Configuración de un servidor SQL Server, leída desde el CSV (sección 5).
/// El usuario/contraseña NUNCA viven aquí ni en el CSV: se toman de appsettings/env vars.
/// </summary>
public class ServerConfig
{
    /// <summary>Dirección IP del servidor SQL Server.</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Nombre de la base de datos en ese servidor (columna BASE del CSV).</summary>
    public string Base { get; set; } = string.Empty;

    /// <summary>
    /// Código de local derivado del nombre de la base de datos (ej. "MAXPOINT_K004" -> "K004").
    /// Es la llave que se usa para agrupar las transacciones de Mongo por servidor.
    /// </summary>
    public string Local { get; set; } = string.Empty;
}