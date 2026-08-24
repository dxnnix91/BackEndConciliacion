namespace Backend.Models;

/// <summary>
/// Fila resultante de la consulta adicional de kiosko usada para revisar pagos "cancelled" en
/// MongoDB (ver ISqlServerService.ObtenerFacturasCanceladasPorCodigosAsync). A diferencia de
/// SqlPayment/ObtenerPagosPorCodigosAsync (la consulta original validada, que exige forma de
/// pago 'DE UNA' en el propio WHERE), esta consulta NO filtra por forma de pago: se trae la
/// factura sin importar cómo quedó registrada, y esa decisión (¿es una alerta real o no?) se
/// toma en C#, igual que ya se hace para domicilios.
/// </summary>
public class FacturaKioskoCanceladaSql
{
    /// <summary>Llave de conciliación contra Mongo.externalReference.</summary>
    public string CodigoApp { get; set; } = string.Empty;

    public int RstId { get; set; }

    /// <summary>Código de factura (ej. "K185F000167444").</summary>
    public string CfacId { get; set; } = string.Empty;

    public decimal CfacTotal { get; set; }

    /// <summary>Forma de pago registrada en SQL (debería decir "DE UNA" para que sea una alerta real).</summary>
    public string FormaPagoDescripcion { get; set; } = string.Empty;

    /// <summary>
    /// Estado de la factura misma (sFactura.std_descripcion, vía cf.IDStatus). El valor clave es
    /// "Entregado"/"Entregada": significa que el pedido fue cobrado y entregado al cliente.
    /// </summary>
    public string FacturaStatus { get; set; } = string.Empty;
}