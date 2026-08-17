namespace Backend.Models;

/// <summary>
/// Fila resultante de la consulta SQL original (sección 6/9 del requerimiento).
/// La consulta original usa SELECT *, pero para la conciliación solo se necesitan
/// estos campos. Si se agregan más columnas a futuro, ampliar aquí sin tocar la
/// consulta base ni sus JOINs.
/// </summary>
public class SqlPayment
{
    /// <summary>Llave de conciliación contra Mongo.externalReference.</summary>
    public string CodigoApp { get; set; } = string.Empty;

    public int RstId { get; set; }

    /// <summary>Código de factura (ej. "K185F000167444"). No es numérico: es local + secuencial.</summary>
    public string CfacId { get; set; } = string.Empty;

    public DateTime FechaOperacion { get; set; }

    public decimal CfacTotal { get; set; }

    public decimal FpfTotalPagar { get; set; }

    public string FormaPagoDescripcion { get; set; } = string.Empty;
}