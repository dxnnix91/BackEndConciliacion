using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Backend.Models;

/// <summary>
/// Documento tal como existe en la colección Mongo "connections" (base "app-domicilio-dev"),
/// que reemplaza tanto a la tabla Restaurante de Azure (AzureCentral) como a la generación
/// dinámica de IP/BASE (LocalesSettings) como fuente de verdad de la conexión de cada local.
/// [BsonIgnoreExtraElements] evita que el driver falle si el documento real tiene campos
/// adicionales no mapeados aquí (ej. webContext, webPort, created_at, etc.).
/// NOTA: username/password vienen encriptados con el esquema de Laravel (Crypt::encrypt) y,
/// por decisión explícita, NO se desencriptan ni se usan: la conexión a SQL Server sigue
/// usando las credenciales compartidas de SqlServerSettings para todos los locales.
/// </summary>
[BsonIgnoreExtraElements]
public class ConexionLocalDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>Equivalente al rst_id que antes se buscaba en la tabla Restaurante de Azure.</summary>
    [BsonElement("restauranteId")]
    public int RestauranteId { get; set; }

    /// <summary>Código de local, ej. "K004". Llave principal de conciliación (== Local).</summary>
    [BsonElement("tiendaName")]
    public string TiendaName { get; set; } = string.Empty;

    [BsonElement("descriptionName")]
    public string DescriptionName { get; set; } = string.Empty;

    /// <summary>IP o hostname del servidor SQL Server de ese local.</summary>
    [BsonElement("serverName")]
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Instancia con nombre de SQL Server, si aplica (vacío la mayoría de las veces).</summary>
    [BsonElement("instanceName")]
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>Puerto TCP de SQL Server. Viene como texto en Mongo (ej. "8433").</summary>
    [BsonElement("port")]
    public string Port { get; set; } = string.Empty;

    /// <summary>Nombre de la base de datos en ese servidor.</summary>
    [BsonElement("databaseName")]
    public string DatabaseName { get; set; } = string.Empty;
}