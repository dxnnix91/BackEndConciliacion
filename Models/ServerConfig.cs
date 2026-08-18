namespace Backend.Models;

/// <summary>
/// Configuración de un servidor SQL Server, obtenida desde la colección Mongo "connections"
/// (ConexionesLocalesService). El usuario/contraseña NUNCA viven aquí: se toman de
/// appsettings/env vars (SqlServerSettings), compartidos para todos los locales.
/// </summary>
public class ServerConfig
{
    /// <summary>Dirección IP o hostname del servidor SQL Server (serverName en Mongo).</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>
    /// Nombre de la base de datos en ese servidor. Siempre se construye como "MAXPOINT_" +
    /// Local (ej. "MAXPOINT_K039"); no se toma del campo databaseName de Mongo, que no es
    /// confiable para esto.
    /// </summary>
    public string Base { get; set; } = string.Empty;

    /// <summary>Código de local (tiendaName en Mongo, ej. "K004").</summary>
    public string Local { get; set; } = string.Empty;

    /// <summary>Puerto TCP de SQL Server (port en Mongo). Vacío si no aplica.</summary>
    public string Puerto { get; set; } = string.Empty;

    /// <summary>Instancia con nombre de SQL Server (instanceName en Mongo). Vacío casi siempre.</summary>
    public string InstanceName { get; set; } = string.Empty;
}