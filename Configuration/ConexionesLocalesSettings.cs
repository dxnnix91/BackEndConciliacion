namespace Backend.Configuration;

/// <summary>
/// Ubicación de la colección de Mongo que reemplaza a Azure (AzureCentralSettings) y a la
/// generación dinámica de IP/BASE (LocalesSettings) como fuente de verdad para la conexión
/// de cada local: un documento por tienda con serverName/port/instanceName/databaseName y su
/// restauranteId. Usa el MISMO MongoClient/connection string que MongoSettings (mismo clúster),
/// pero apunta a otra base de datos y otra colección.
/// </summary>
public class ConexionesLocalesSettings
{
    public const string SectionName = "ConexionesLocales";

    public string Database { get; set; } = "app-domicilio-dev";

    public string Collection { get; set; } = "connections";

    /// <summary>
    /// Minutos que se mantiene en memoria el caché de conexiones antes de refrescarlo desde
    /// Mongo otra vez. Es una colección de configuración pequeña que casi no cambia, así que
    /// se cachea agresivamente para no pagar un round-trip a Mongo por cada local en cada
    /// conciliación (esto es justo lo que se buscaba "agilizar").
    /// </summary>
    public int CacheMinutos { get; set; } = 5;
}