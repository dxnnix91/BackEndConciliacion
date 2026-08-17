namespace Backend.Models;

/// <summary>Resumen general de una conciliación (sección 15), sobre un rango de fechas.</summary>
public class ConciliacionResumen
{
    public string FechaInicio { get; set; } = string.Empty;
    public string FechaFin { get; set; } = string.Empty;
    public int TotalMongo { get; set; }
    public int TotalSql { get; set; }
    public int Conciliados { get; set; }
    public int FaltantesSql { get; set; }
    public int FaltantesMongo { get; set; }
    public int Diferencias { get; set; }
    public int ErroresConexion { get; set; }
    public int ErroresSql { get; set; }
    public int ConfiguracionNoEncontrada { get; set; }
    public int LocalesProcesados { get; set; }
}