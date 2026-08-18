using Backend.Models;

namespace Backend.DTOs;

/// <summary>Response de POST /api/conciliacion-domicilio/ejecutar.</summary>
public class ConciliacionDomicilioResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ConciliacionDomicilioResumen? Resumen { get; set; }
    public List<ConciliacionDomicilioResult> Resultados { get; set; } = new();
}