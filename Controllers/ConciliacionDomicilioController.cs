using Backend.DTOs;
using Backend.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Conciliación de pedidos a domicilio (colección Mongo "request_payments" vs facturas de SQL
/// Server), separada de ConciliacionController (kiosko): corre de forma independiente y no
/// comparte estado de ejecución con la conciliación de kiosko.
/// </summary>
[ApiController]
[Route("api/conciliacion-domicilio")]
public class ConciliacionDomicilioController : ControllerBase
{
    private readonly IConciliacionDomicilioService _conciliacionDomicilioService;
    private readonly ILogger<ConciliacionDomicilioController> _logger;

    public ConciliacionDomicilioController(IConciliacionDomicilioService conciliacionDomicilioService, ILogger<ConciliacionDomicilioController> logger)
    {
        _conciliacionDomicilioService = conciliacionDomicilioService;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta la conciliación de pedidos a domicilio para el rango de fechas indicado.
    /// Mientras se ejecuta, el frontend puede consultar el progreso con GET /estado.
    /// </summary>
    [HttpPost("ejecutar")]
    public async Task<ActionResult<ConciliacionDomicilioResponse>> Ejecutar([FromBody] ConciliacionRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(request.FechaInicio, out var fechaInicio))
        {
            return BadRequest(new ConciliacionDomicilioResponse
            {
                Success = false,
                Error = "El campo 'fechaInicio' es obligatorio y debe tener formato yyyy-MM-dd."
            });
        }

        var fechaFinTexto = string.IsNullOrWhiteSpace(request.FechaFin) ? request.FechaInicio : request.FechaFin;
        if (!DateOnly.TryParse(fechaFinTexto, out var fechaFin))
        {
            return BadRequest(new ConciliacionDomicilioResponse
            {
                Success = false,
                Error = "El campo 'fechaFin' debe tener formato yyyy-MM-dd."
            });
        }

        if (fechaFin < fechaInicio)
        {
            return BadRequest(new ConciliacionDomicilioResponse
            {
                Success = false,
                Error = "'fechaFin' no puede ser anterior a 'fechaInicio'."
            });
        }

        try
        {
            var respuesta = await _conciliacionDomicilioService.EjecutarAsync(fechaInicio, fechaFin, cancellationToken);
            return respuesta.Success ? Ok(respuesta) : StatusCode(500, respuesta);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ConciliacionDomicilioResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>Progreso de la conciliación de domicilios en curso (o de la última finalizada).</summary>
    [HttpGet("estado")]
    public ActionResult<EstadoConciliacionDomicilioDto> Estado()
    {
        return Ok(_conciliacionDomicilioService.ObtenerEstado());
    }
}