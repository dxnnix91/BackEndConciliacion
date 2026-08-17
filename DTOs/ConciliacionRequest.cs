namespace Backend.DTOs;

/// <summary>Request de POST /api/conciliacion/ejecutar (sección 18), ahora con rango de fechas.</summary>
public class ConciliacionRequest
{
    /// <summary>Fecha de inicio del rango a conciliar (inclusive), formato "yyyy-MM-dd".</summary>
    public string FechaInicio { get; set; } = string.Empty;

    /// <summary>
    /// Fecha de fin del rango a conciliar (inclusive), formato "yyyy-MM-dd".
    /// Si se omite, se usa el mismo valor que FechaInicio (conciliación de un solo día).
    /// </summary>
    public string? FechaFin { get; set; }
}