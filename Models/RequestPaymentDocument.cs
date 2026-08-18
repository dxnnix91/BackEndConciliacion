using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Backend.Models;

/// <summary>
/// Documento de la colección Mongo "request_payments" (base "deuna-service"): solicitudes de
/// pago de pedidos a domicilio. Solo se mapean los campos relevantes para la conciliación;
/// [BsonIgnoreExtraElements] evita que el driver falle por campos no mapeados (products,
/// client, subsidyId, pinCode, withSubsidy, headOrderId, etc.).
/// </summary>
[BsonIgnoreExtraElements]
public class RequestPaymentDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>Número de factura (cfac_id) del lado de SQL Server. Llave de conciliación.</summary>
    [BsonElement("invoiceId")]
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Ej. "DEUNA-K028-13291-1786927680". El segundo segmento es el código de local.</summary>
    [BsonElement("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [BsonElement("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [BsonElement("pointOfSale")]
    public string PointOfSale { get; set; } = string.Empty;

    /// <summary>Monto ya en dólares (a diferencia de kiosko, que lo guarda en centavos).</summary>
    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("detail")]
    public string Detail { get; set; } = string.Empty;

    /// <summary>Ej. "Approved", "Cancel", "Refunded". Un solo status por documento/requestId.</summary>
    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("store")]
    public RequestPaymentStore? Store { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Local derivado de requestId (ej. "DEUNA-K028-..." -> "K028"), con respaldo vía
    /// store.id/IRestauranteCentralService si requestId no trae un local reconocible. No viene
    /// de Mongo directamente; se calcula en MongoDomicilioService.
    /// </summary>
    [BsonIgnore]
    public string Local { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
public class RequestPaymentStore
{
    /// <summary>Equivalente al restauranteId/rst_id, usado como respaldo para encontrar el local.</summary>
    [BsonElement("id")]
    public int Id { get; set; }

    [BsonElement("vendorId")]
    public int VendorId { get; set; }
}