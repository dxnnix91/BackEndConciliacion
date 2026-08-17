namespace Backend.Configuration;

/// <summary>
/// Conexión a la base central en Azure SQL (MAXPOINT) que tiene la tabla Restaurante
/// completa (una fila por cada tienda), a diferencia de la base de cada tienda individual
/// que solo tiene su propia fila. Se usa únicamente como respaldo cuando no se puede
/// determinar el local desde el externalReference de Mongo.
/// </summary>
public class AzureCentralSettings
{
    public const string SectionName = "AzureCentral";

    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 30;
}