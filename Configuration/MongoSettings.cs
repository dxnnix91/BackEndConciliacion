namespace Backend.Configuration;

/// <summary>
/// Configuración de conexión a MongoDB.
/// Se puebla desde appsettings.json / variables de entorno, por ejemplo:
///   Mongo__ConnectionString
///   Mongo__Database
///   Mongo__Collection
/// MongoDB ya existe externamente: aquí solo se define cómo conectarse.
/// </summary>
public class MongoSettings
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string Collection { get; set; } = "payments";
}