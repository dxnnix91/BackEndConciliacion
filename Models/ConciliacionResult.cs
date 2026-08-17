namespace Backend.Models;

/// <summary>Detalle de conciliación de una transacción (sección 16).</summary>
public class ConciliacionResult
{
    public string Local { get; set; } = string.Empty;
    public string BranchOffice { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public string? CodigoApp { get; set; }
    public EstadoConciliacion Estado { get; set; }

    public string? MongoStatus { get; set; }
    public decimal? MongoAmount { get; set; }
    public decimal? MongoOrderDetailTotal { get; set; }
    public decimal? SqlAmount { get; set; }
    public decimal? SqlFpfTotalPagar { get; set; }

    /// <summary>Código de factura (cfac_id) del lado de SQL Server, para mostrar en el detalle. Es un código alfanumérico (ej. "K185F000167444"), no un número.</summary>
    public string? CfacId { get; set; }

    public DateTime? FechaMongo { get; set; }
    public DateTime? FechaSql { get; set; }

    public string Mensaje { get; set; } = string.Empty;
}