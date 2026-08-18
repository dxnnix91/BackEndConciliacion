namespace Backend.Models;

/// <summary>Detalle de conciliación de una solicitud de pago a domicilio.</summary>
public class ConciliacionDomicilioResult
{
    public string Local { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public EstadoConciliacionDomicilio Estado { get; set; }

    public string? MongoStatus { get; set; }
    public decimal? MongoAmount { get; set; }

    public decimal? SqlCfacTotal { get; set; }
    public string? FormaPagoDescripcion { get; set; }
    public string? FormaPagoStatus { get; set; }
    public string? FacturaStatus { get; set; }

    public DateTime? FechaMongo { get; set; }

    public string Mensaje { get; set; } = string.Empty;
}