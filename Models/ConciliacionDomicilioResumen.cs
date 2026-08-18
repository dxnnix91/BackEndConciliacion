namespace Backend.Models;

/// <summary>Resumen general de una conciliación de domicilios, sobre un rango de fechas.</summary>
public class ConciliacionDomicilioResumen
{
    public string FechaInicio { get; set; } = string.Empty;
    public string FechaFin { get; set; } = string.Empty;
    public int TotalMongo { get; set; }
    public int Conciliados { get; set; }
    public int OrdenesSinFactura { get; set; }
    public int Diferencias { get; set; }
    public int FormaPagoIncorrecta { get; set; }
    public int FacturaNoEntregada { get; set; }
    public int FacturaConPagoCancelado { get; set; }
    public int SinNovedad { get; set; }
    public int ConfiguracionNoEncontrada { get; set; }
    public int ErroresConexion { get; set; }
    public int ErroresSql { get; set; }
    public int LocalesProcesados { get; set; }
}