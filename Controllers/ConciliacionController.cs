using Backend.DTOs;
using Backend.Helpers;
using Backend.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/conciliacion")]
public class ConciliacionController : ControllerBase
{
    private readonly IConciliacionService _conciliacionService;
    private readonly ILogger<ConciliacionController> _logger;

    public ConciliacionController(IConciliacionService conciliacionService, ILogger<ConciliacionController> logger)
    {
        _conciliacionService = conciliacionService;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta la conciliación de pagos para el rango de fechas indicado (sección 18).
    /// Mientras se ejecuta, el frontend puede consultar el progreso con GET /estado.
    /// </summary>
    [HttpPost("ejecutar")]
    public async Task<ActionResult<ConciliacionResponse>> Ejecutar([FromBody] ConciliacionRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(request.FechaInicio, out var fechaInicio))
        {
            return BadRequest(new ConciliacionResponse
            {
                Success = false,
                Error = "El campo 'fechaInicio' es obligatorio y debe tener formato yyyy-MM-dd."
            });
        }

        // Si no envían fechaFin, se concilia un solo día (fechaInicio == fechaFin).
        var fechaFinTexto = string.IsNullOrWhiteSpace(request.FechaFin) ? request.FechaInicio : request.FechaFin;
        if (!DateOnly.TryParse(fechaFinTexto, out var fechaFin))
        {
            return BadRequest(new ConciliacionResponse
            {
                Success = false,
                Error = "El campo 'fechaFin' debe tener formato yyyy-MM-dd."
            });
        }

        if (fechaFin < fechaInicio)
        {
            return BadRequest(new ConciliacionResponse
            {
                Success = false,
                Error = "'fechaFin' no puede ser anterior a 'fechaInicio'."
            });
        }

        try
        {
            var respuesta = await _conciliacionService.EjecutarAsync(fechaInicio, fechaFin, cancellationToken);
            return respuesta.Success ? Ok(respuesta) : StatusCode(500, respuesta);
        }
        catch (InvalidOperationException ex)
        {
            // Sección 19: no permitir ejecuciones simultáneas.
            return Conflict(new ConciliacionResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Lista los locales configurados en la colección Mongo "connections", sin exponer
    /// credenciales (sección 25).
    /// </summary>
    [HttpGet("servidores")]
    public async Task<ActionResult<IReadOnlyList<ServidorDto>>> Servidores(CancellationToken cancellationToken)
    {
        return Ok(await _conciliacionService.ObtenerServidoresAsync(cancellationToken));
    }

    /// <summary>
    /// Progreso de la conciliación en curso (o de la última finalizada), para polling desde
    /// Angular (secciones 19-20). Dejado preparado para reemplazar/complementar con SignalR.
    /// </summary>
    [HttpGet("estado")]
    public ActionResult<EstadoConciliacionDto> Estado()
    {
        return Ok(_conciliacionService.ObtenerEstado());
    }

    /// <summary>
    /// Genera el Excel de exportación (sección 26) a partir del resumen/detalle que el
    /// frontend ya tiene en pantalla (resultado de la última ejecución mostrada).
    /// </summary>
    [HttpPost("exportar")]
    public IActionResult Exportar([FromBody] ExportarExcelRequest request)
    {
        var bytes = ExcelExportHelper.Generar(request.Resumen, request.Resultados);
        var nombreArchivo = request.Resumen is null
            ? "conciliacion"
            : request.Resumen.FechaInicio == request.Resumen.FechaFin
                ? $"conciliacion_{request.Resumen.FechaInicio}"
                : $"conciliacion_{request.Resumen.FechaInicio}_a_{request.Resumen.FechaFin}";

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{nombreArchivo}.xlsx");
    }
}