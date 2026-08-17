using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Backend.Models;

/// <summary>
/// Representa un documento de transacción tal como existe en MongoDB.
/// Solo se mapean los campos relevantes para la conciliación (sección 3 y 9 del requerimiento).
/// [BsonIgnoreExtraElements] evita que el driver falle si el documento real tiene campos
/// adicionales que no están mapeados aquí (ej. providerTransactionId, y otros que puedan existir).
/// </summary>
[BsonIgnoreExtraElements]
public class MongoPayment
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("externalReference")]
    public string ExternalReference { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("orderDetail")]
    public MongoOrderDetail? OrderDetail { get; set; }

    [BsonElement("metadataCreatePayment")]
    public MongoMetadataCreatePayment? MetadataCreatePayment { get; set; }

    [BsonElement("providerData")]
    public MongoProviderData? ProviderData { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Local derivado de externalReference (ej: "EC-K004-33-..." -> "K004").
    /// No proviene de Mongo directamente; se calcula en MongoService.
    /// </summary>
    [BsonIgnore]
    public string Local { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
public class MongoOrderDetail
{
    [BsonElement("price")]
    public MongoPrice? Price { get; set; }
}

[BsonIgnoreExtraElements]
public class MongoPrice
{
    [BsonElement("total")]
    public decimal Total { get; set; }

    [BsonElement("tax")]
    public decimal Tax { get; set; }
}

[BsonIgnoreExtraElements]
public class MongoMetadataCreatePayment
{
    [BsonElement("branchOffice")]
    public string BranchOffice { get; set; } = string.Empty;

    [BsonElement("pointOfSale")]
    public string PointOfSale { get; set; } = string.Empty;

    [BsonElement("orderSaleId")]
    public string OrderSaleId { get; set; } = string.Empty;
}

/// <summary>
/// Campo adicional mencionado en la sección 9 (providerData.amount) para validaciones
/// de montos, sin asumir que equivale a otros campos monetarios.
/// </summary>
[BsonIgnoreExtraElements]
public class MongoProviderData
{
    [BsonElement("amount")]
    public decimal? Amount { get; set; }
}