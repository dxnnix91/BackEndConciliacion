namespace Backend.Configuration;

/// <summary>
/// Ubicación de la colección Mongo de pedidos a domicilio ("request_payments", base
/// "deuna-service"). Usa el MISMO MongoClient/connection string que MongoSettings (mismo
/// clúster), pero apunta a otra base de datos y otra colección.
/// </summary>
public class DomicilioSettings
{
    public const string SectionName = "Domicilio";

    public string Database { get; set; } = "deuna-service";

    public string Collection { get; set; } = "request_payments";
}