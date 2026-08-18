namespace Backend.Models;

/// <summary>Estados posibles de cada solicitud de pago a domicilio conciliada.</summary>
public enum EstadoConciliacionDomicilio
{
    /// <summary>Aprobado en Mongo, factura entregada, forma de pago "DE UNA" y monto coincide.</summary>
    CONCILIADO,

    /// <summary>Aprobado en Mongo, pero no existe ninguna factura con ese invoiceId en SQL Server.</summary>
    ORDEN_SIN_FACTURA,

    /// <summary>Aprobado en Mongo, factura encontrada y entregada, pero el monto no coincide.</summary>
    DIFERENCIA_MONTO,

    /// <summary>
    /// Aprobado en Mongo, factura entregada, pero registrada con una forma de pago distinta a
    /// "DE UNA" (bug conocido del sistema local). El dinero está bien, pero hay que corregir el
    /// registro de forma de pago.
    /// </summary>
    FORMA_PAGO_INCORRECTA,

    /// <summary>
    /// Aprobado en Mongo, factura encontrada, pero su estado no es "Entregado" (ej. anulada u
    /// otro estado): el pedido no quedó cobrado y entregado al cliente.
    /// </summary>
    FACTURA_NO_ENTREGADA,

    /// <summary>
    /// El pago en Mongo NO está aprobado (Cancel, Refunded, etc.), pero sí existe una factura
    /// generada en SQL Server para ese invoiceId: se facturó algo que en Deuna quedó cancelado o
    /// reembolsado.
    /// </summary>
    FACTURA_CON_PAGO_CANCELADO,

    /// <summary>
    /// El pago en Mongo no está aprobado y tampoco hay factura en SQL: todo consistente, no
    /// requiere ninguna acción.
    /// </summary>
    SIN_NOVEDAD,

    /// <summary>No se pudo determinar/conectar al servidor SQL del local (mismo caso que en kiosko).</summary>
    CONFIGURACION_NO_ENCONTRADA,

    ERROR_CONEXION,

    ERROR_SQL
}