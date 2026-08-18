namespace Backend.Models;

/// <summary>
/// Fila resultante de la consulta de facturas para domicilios (Cabecera_Factura +
/// Formapago_Factura + Formapago + dos joins a Status: uno por la forma de pago y otro por la
/// factura misma). A diferencia de kiosko, aquí NO se filtra por forma de pago en la consulta
/// SQL: se trae la factura sin importar cómo quedó registrada, porque el sistema local a veces
/// la asigna mal (ej. "EFECTIVO" en vez de "DE UNA") aunque el pago real fue por Deuna.
/// </summary>
public class FacturaDomicilioSql
{
    /// <summary>Llave de conciliación contra Mongo.invoiceId.</summary>
    public string CfacId { get; set; } = string.Empty;

    public int RstId { get; set; }

    public decimal CfacTotal { get; set; }

    public string FpfCodigo { get; set; } = string.Empty;

    /// <summary>Forma de pago registrada en SQL (debería decir "DE UNA"; a veces el sistema la asigna mal).</summary>
    public string FormaPagoDescripcion { get; set; } = string.Empty;

    /// <summary>Estado del registro de Formapago_Factura (sFormaPago.std_descripcion). Informativo.</summary>
    public string FormaPagoStatus { get; set; } = string.Empty;

    /// <summary>
    /// Estado de la factura misma (sFactura.std_descripcion, vía cf.IDStatus). El valor clave es
    /// "Entregado": significa que el pedido fue cobrado y entregado al cliente.
    /// </summary>
    public string FacturaStatus { get; set; } = string.Empty;
}