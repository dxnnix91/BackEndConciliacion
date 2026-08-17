using Backend.Models;

namespace Backend.DTOs;

/// <summary>Response de POST /api/conciliacion/ejecutar (sección 18).</summary>
public class ConciliacionResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ConciliacionResumen? Resumen { get; set; }
    public List<ConciliacionResult> Resultados { get; set; } = new();
}