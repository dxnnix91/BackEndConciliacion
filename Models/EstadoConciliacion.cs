namespace Backend.Models;

/// <summary>Estados posibles de cada registro conciliado (sección 14).</summary>
public enum EstadoConciliacion
{
    CONCILIADO,
    FALTA_SQL,
    FALTA_MONGO,
    DIFERENCIA_MONTO,
    ERROR_CONEXION,
    ERROR_SQL,
    CONFIGURACION_NO_ENCONTRADA
}

/// <summary>Estado general de ejecución de una conciliación (para GET /estado).</summary>
public enum EstadoEjecucion
{
    INACTIVO,
    EN_PROGRESO,
    FINALIZADO,
    FINALIZADO_CON_ERRORES
}