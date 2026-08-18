using System.Collections.Concurrent;
using Backend.Configuration;
using Backend.DTOs;
using Backend.Models;
using Backend.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <summary>
/// Orquesta el flujo de conciliación de pedidos a domicilio: trae TODAS las solicitudes de
/// Mongo (sin filtrar por status) del rango de fechas, las agrupa por local, y por cada local
/// busca en SQL Server (reutilizando la misma colección "connections" que la conciliación de
/// kiosko, vía IServidorService) las facturas correspondientes a esos invoiceId, clasificando
/// cada solicitud según el cruce entre el status de Mongo y el estado real de la factura.
/// Registrado como singleton, igual que ConciliacionService, para mantener su propio progreso
/// independiente (una conciliación de domicilios en curso no bloquea ni se confunde con una de
/// kiosko corriendo al mismo tiempo).
/// </summary>
public class ConciliacionDomicilioService : IConciliacionDomicilioService
{
    private readonly IMongoDomicilioService _mongoDomicilioService;
    private readonly ISqlServerService _sqlServerService;
    private readonly IServidorService _servidorService;
    private readonly SqlServerSettings _sqlServerSettings;
    private readonly ILogger<ConciliacionDomicilioService> _logger;

    private readonly object _lock = new();
    private bool _ejecutando;
    private EstadoEjecucion _estado = EstadoEjecucion.INACTIVO;
    private string? _fechaInicioActual;
    private string? _fechaFinActual;
    private int _localesTotal;
    private int _localesProcesados;
    private int _solicitudesTotal;
    private int _solicitudesProcesadas;
    private readonly HashSet<string> _localesEnProceso = new();
    private DateTime? _iniciadoEn;
    private DateTime? _finalizadoEn;
    private ConciliacionDomicilioResumen? _resumen;
    private int _solicitudesOmitidas;

    private const decimal ToleranciaMonto = 0.01m;

    // La base de datos concuerda el género gramatical con "factura" (femenino) y guarda
    // "Entregada", no "Entregado" como se había asumido inicialmente. Se aceptan ambas formas
    // por robustez, por si algún local/versión distinta del sistema local usa la otra.
    private static readonly HashSet<string> EstadosFacturaEntregada = new(StringComparer.OrdinalIgnoreCase)
    {
        "Entregado",
        "Entregada"
    };

    private const string FormaPagoEsperada = "DE UNA";
    private const string MongoStatusAprobado = "Approved";

    // Transacciones que todavía están en curso (recién solicitadas en Mongo, o con la factura/
    // forma de pago aún "Pendiente" en SQL): no se han resuelto todavía, así que no se
    // conciliar, no cuentan como problema ni como conciliadas — se excluyen del resultado.
    private const string MongoStatusSolicitado = "Requested";
    private const string EstadoPendiente = "Pendiente";

    public ConciliacionDomicilioService(
        IMongoDomicilioService mongoDomicilioService,
        ISqlServerService sqlServerService,
        IServidorService servidorService,
        IOptions<SqlServerSettings> sqlServerSettings,
        ILogger<ConciliacionDomicilioService> logger)
    {
        _mongoDomicilioService = mongoDomicilioService;
        _sqlServerService = sqlServerService;
        _servidorService = servidorService;
        _sqlServerSettings = sqlServerSettings.Value;
        _logger = logger;
    }

    public async Task<ConciliacionDomicilioResponse> EjecutarAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default)
    {
        if (fechaFin < fechaInicio)
        {
            throw new ArgumentException("La fecha de fin no puede ser anterior a la fecha de inicio.");
        }

        lock (_lock)
        {
            if (_ejecutando)
            {
                throw new InvalidOperationException("Ya existe una conciliación de domicilios en ejecución.");
            }

            _ejecutando = true;
            _estado = EstadoEjecucion.EN_PROGRESO;
            _fechaInicioActual = fechaInicio.ToString("yyyy-MM-dd");
            _fechaFinActual = fechaFin.ToString("yyyy-MM-dd");
            _localesTotal = 0;
            _localesProcesados = 0;
            _solicitudesTotal = 0;
            _solicitudesProcesadas = 0;
            _localesEnProceso.Clear();
            _iniciadoEn = DateTime.UtcNow;
            _finalizadoEn = null;
            _resumen = null;
            _solicitudesOmitidas = 0;
        }

        var resultados = new ConcurrentQueue<ConciliacionDomicilioResult>();

        try
        {
            _logger.LogInformation("=== Inicio de conciliación de domicilios entre {FechaInicio} y {FechaFin} ===", fechaInicio, fechaFin);

            var solicitudes = await _mongoDomicilioService.ObtenerSolicitudesAsync(fechaInicio, fechaFin, cancellationToken);
            var grupos = solicitudes.GroupBy(s => s.Local).ToList();

            lock (_lock)
            {
                _solicitudesTotal = solicitudes.Count;
                _localesTotal = grupos.Count;
            }

            _logger.LogInformation("Locales encontrados en Mongo (domicilio): {Cantidad}", grupos.Count);

            // Mismo grado de paralelismo/aislamiento por local que la conciliación de kiosko.
            var maxGradoParalelismo = Math.Max(1, _sqlServerSettings.MaxGradoParalelismo);

            await Parallel.ForEachAsync(
                grupos,
                new ParallelOptions { MaxDegreeOfParallelism = maxGradoParalelismo, CancellationToken = cancellationToken },
                async (grupo, ct) => await ProcesarLocalAsync(grupo.Key, grupo.ToList(), resultados, ct));

            var resultadosFinales = resultados.ToList();
            var resumen = ConstruirResumen(fechaInicio, fechaFin, grupos.Count, resultadosFinales);

            lock (_lock)
            {
                _resumen = resumen;
                _estado = resumen.ErroresConexion + resumen.ErroresSql > 0
                    ? EstadoEjecucion.FINALIZADO_CON_ERRORES
                    : EstadoEjecucion.FINALIZADO;
                _finalizadoEn = DateTime.UtcNow;
            }

            _logger.LogInformation(
                "=== Fin de conciliación de domicilios entre {FechaInicio} y {FechaFin}: {@Resumen} (omitidas del reporte: {Omitidas}) ===",
                fechaInicio, fechaFin, resumen, _solicitudesOmitidas);

            return new ConciliacionDomicilioResponse
            {
                Success = true,
                Resumen = resumen,
                Resultados = resultadosFinales
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado ejecutando la conciliación de domicilios entre {FechaInicio} y {FechaFin}", fechaInicio, fechaFin);

            lock (_lock)
            {
                _estado = EstadoEjecucion.FINALIZADO_CON_ERRORES;
                _finalizadoEn = DateTime.UtcNow;
            }

            return new ConciliacionDomicilioResponse
            {
                Success = false,
                Error = ex.Message,
                Resultados = resultados.ToList()
            };
        }
        finally
        {
            lock (_lock)
            {
                _ejecutando = false;
                _localesEnProceso.Clear();
            }
        }
    }

    /// <summary>
    /// Procesa un único local: consulta las facturas de SQL Server para los invoiceId de ese
    /// local y clasifica cada solicitud. Igual que en kiosko, corre en su propia tarea dentro
    /// de Parallel.ForEachAsync, con su propio try/catch para que un local lento/caído no
    /// bloquee a los demás.
    /// </summary>
    private async Task ProcesarLocalAsync(
        string local,
        List<RequestPaymentDocument> solicitudesDelLocal,
        ConcurrentQueue<ConciliacionDomicilioResult> resultados,
        CancellationToken cancellationToken)
    {
        MarcarLocalEnProceso(local);
        try
        {
            if (string.IsNullOrWhiteSpace(local))
            {
                _logger.LogWarning("Se encontraron {Cantidad} solicitudes de domicilio sin local identificable en requestId", solicitudesDelLocal.Count);
                foreach (var resultado in solicitudesDelLocal.Select(s => ConstruirResultadoSinConfiguracion(s, local: "DESCONOCIDO")))
                {
                    resultados.Enqueue(resultado);
                }
                return;
            }

            var servidor = await _servidorService.BuscarPorLocalAsync(local, cancellationToken);
            if (servidor is null)
            {
                _logger.LogWarning("Local {Local} no tiene configuración de servidor válida (formato de local irreconocible)", local);
                foreach (var resultado in solicitudesDelLocal.Select(s => ConstruirResultadoSinConfiguracion(s, local)))
                {
                    resultados.Enqueue(resultado);
                }
                return;
            }

            try
            {
                var invoiceIds = solicitudesDelLocal
                    .Select(s => s.InvoiceId)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var facturas = await _sqlServerService.ObtenerFacturasPorInvoiceIdsAsync(servidor, invoiceIds, cancellationToken);

                var facturasPorInvoiceId = new Dictionary<string, FacturaDomicilioSql>(StringComparer.OrdinalIgnoreCase);
                foreach (var factura in facturas)
                {
                    if (!string.IsNullOrWhiteSpace(factura.CfacId) && !facturasPorInvoiceId.ContainsKey(factura.CfacId))
                    {
                        facturasPorInvoiceId[factura.CfacId] = factura;
                    }
                }

                // El cliente puede reintentar el pago varias veces para el mismo invoiceId (un
                // primer intento que se cancela/rechaza, seguido de otro requestId que sí queda
                // Approved). Si alguno de los intentos de un invoiceId quedó Approved, ese es el
                // que se concilia contra la factura; los demás intentos de ese mismo invoiceId
                // son reintentos fallidos normales del flujo de pago, no una alerta real.
                var invoiceIdsConAprobado = solicitudesDelLocal
                    .Where(s => string.Equals(s.Status, MongoStatusAprobado, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.InvoiceId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var solicitud in solicitudesDelLocal)
                {
                    var aprobado = string.Equals(solicitud.Status, MongoStatusAprobado, StringComparison.OrdinalIgnoreCase);

                    // Se omite: el pago recién se solicitó en Mongo, todavía no se resolvió
                    // (no llegó a Approved/Cancel/etc.), así que no tiene sentido conciliarlo aún.
                    if (string.Equals(solicitud.Status, MongoStatusSolicitado, StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref _solicitudesOmitidas);
                        continue;
                    }

                    // Se omite: este intento no quedó aprobado, pero otro requestId para el
                    // mismo invoiceId sí quedó Approved — es el reintento fallido previo, no una
                    // factura con pago cancelado real.
                    if (!aprobado && !string.IsNullOrWhiteSpace(solicitud.InvoiceId) && invoiceIdsConAprobado.Contains(solicitud.InvoiceId))
                    {
                        Interlocked.Increment(ref _solicitudesOmitidas);
                        continue;
                    }

                    var factura = facturasPorInvoiceId.TryGetValue(solicitud.InvoiceId, out var f) ? f : null;

                    // Se omite: el pago no quedó aprobado en Mongo (Cancel/etc.) y tampoco existe
                    // ninguna factura en SQL Server para ese invoiceId. Es un caso consistente (el
                    // cliente canceló y nunca se generó nada en el local), pero no aporta valor
                    // verlo en el reporte — no es una alerta ni algo que revisar.
                    if (!aprobado && factura is null)
                    {
                        Interlocked.Increment(ref _solicitudesOmitidas);
                        continue;
                    }

                    // Se omite: la factura o su forma de pago sigue "Pendiente" en SQL (la
                    // transacción todavía se está realizando), sin importar el status en Mongo.
                    if (factura is not null && EsPendiente(factura))
                    {
                        Interlocked.Increment(ref _solicitudesOmitidas);
                        continue;
                    }

                    resultados.Enqueue(Clasificar(solicitud, factura, local));
                }
            }
            catch (Exception ex)
            {
                var estadoError = ClasificarError(ex);
                _logger.LogError(ex,
                    "Error procesando local {Local} (IP={Ip}, Base={Base}) en domicilios: {Estado}",
                    local, servidor.Ip, servidor.Base, estadoError);

                foreach (var resultado in solicitudesDelLocal.Select(s => ConstruirResultado(s, null, local, estadoError, ex.Message)))
                {
                    resultados.Enqueue(resultado);
                }
            }
        }
        finally
        {
            ActualizarProgresoLocal(local, solicitudesDelLocal.Count);
        }
    }

    /// <summary>
    /// "Pendiente" puede venir tanto en el estado de la factura (factura_status) como en el de
    /// la forma de pago (formapago_status): si cualquiera de los dos lo dice, la transacción
    /// todavía se está procesando en el sistema local y no debe conciliarse todavía.
    /// </summary>
    private static bool EsPendiente(FacturaDomicilioSql factura)
        => string.Equals(factura.FacturaStatus, EstadoPendiente, StringComparison.OrdinalIgnoreCase)
           || string.Equals(factura.FormaPagoStatus, EstadoPendiente, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cruza el status de Mongo contra la existencia/estado de la factura en SQL Server.
    /// Cuando el pago está Approved en Mongo y la factura existe, la prioridad es: primero se
    /// revisa si quedó "Entregado" (lo más importante: cobrado y entregado al cliente); si no,
    /// se revisa la forma de pago; y por último el monto.
    /// </summary>
    private static ConciliacionDomicilioResult Clasificar(RequestPaymentDocument solicitud, FacturaDomicilioSql? factura, string local)
    {
        var aprobadoEnMongo = string.Equals(solicitud.Status, MongoStatusAprobado, StringComparison.OrdinalIgnoreCase);

        if (!aprobadoEnMongo)
        {
            // El caso "no aprobado y sin factura en SQL" ya se filtra antes de llegar aquí (se
            // omite del reporte en ProcesarLocalAsync, ver _solicitudesOmitidas). Este método solo
            // recibe solicitudes no aprobadas cuando SÍ existe una factura para ese invoiceId.
            if (factura is null)
            {
                return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.SIN_NOVEDAD,
                    $"Pago en estado '{solicitud.Status}' en Mongo y sin factura en SQL Server: consistente.");
            }

            // Si la factura se pagó con un medio distinto a "DE UNA" (débito, efectivo, etc.),
            // el cliente pagó en la tienda por otro medio, sin relación con el pago cancelado/
            // rechazado en la app: es correcto, no una alerta. Solo es un problema real si la
            // factura quedó registrada como "DE UNA" pese a que en Mongo el pago no se aprobó.
            if (!string.Equals(factura.FormaPagoDescripcion, FormaPagoEsperada, StringComparison.OrdinalIgnoreCase))
            {
                return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.SIN_NOVEDAD,
                    $"Pago en estado '{solicitud.Status}' en Mongo, pero la factura encontrada (cfac_id='{factura.CfacId}') se pagó con '{factura.FormaPagoDescripcion}', no 'DE UNA': consistente.");
            }

            return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.FACTURA_CON_PAGO_CANCELADO,
                $"Pago en estado '{solicitud.Status}' en Mongo, pero existe una factura generada como 'DE UNA' en SQL Server (cfac_id='{factura.CfacId}').");
        }

        if (factura is null)
        {
            return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.ORDEN_SIN_FACTURA,
                "Pago aprobado en Mongo pero no se encontró ninguna factura con ese invoiceId en SQL Server.");
        }

        if (string.IsNullOrWhiteSpace(factura.FacturaStatus) || !EstadosFacturaEntregada.Contains(factura.FacturaStatus))
        {
            return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.FACTURA_NO_ENTREGADA,
                $"Pago aprobado en Mongo, factura encontrada, pero su estado es '{factura.FacturaStatus}' (no 'Entregada'/'Entregado').");
        }

        if (!string.Equals(factura.FormaPagoDescripcion, FormaPagoEsperada, StringComparison.OrdinalIgnoreCase))
        {
            return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.FORMA_PAGO_INCORRECTA,
                $"Factura entregada correctamente, pero registrada con forma de pago '{factura.FormaPagoDescripcion}' en vez de 'DE UNA'.");
        }

        var montoCoincide = Math.Abs(solicitud.Amount - factura.CfacTotal) <= ToleranciaMonto;
        if (!montoCoincide)
        {
            return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.DIFERENCIA_MONTO,
                "Factura entregada y forma de pago correcta, pero el monto no coincide.");
        }

        return ConstruirResultado(solicitud, factura, local, EstadoConciliacionDomicilio.CONCILIADO, "Conciliación correcta.");
    }

    private static ConciliacionDomicilioResult ConstruirResultado(
        RequestPaymentDocument solicitud, FacturaDomicilioSql? factura, string local, EstadoConciliacionDomicilio estado, string mensaje)
    {
        return new ConciliacionDomicilioResult
        {
            Local = local,
            RequestId = solicitud.RequestId,
            InvoiceId = solicitud.InvoiceId,
            Estado = estado,
            MongoStatus = solicitud.Status,
            MongoAmount = solicitud.Amount,
            SqlCfacTotal = factura?.CfacTotal,
            FormaPagoDescripcion = factura?.FormaPagoDescripcion,
            FormaPagoStatus = factura?.FormaPagoStatus,
            FacturaStatus = factura?.FacturaStatus,
            FechaMongo = solicitud.CreatedAt,
            Mensaje = mensaje
        };
    }

    private static ConciliacionDomicilioResult ConstruirResultadoSinConfiguracion(RequestPaymentDocument solicitud, string local)
    {
        return ConstruirResultado(solicitud, null, local, EstadoConciliacionDomicilio.CONFIGURACION_NO_ENCONTRADA,
            $"No se pudo generar la conexión SQL para el local '{local}' (formato de local irreconocible).");
    }

    /// <summary>Misma heurística que en kiosko (SqlServerService/ConciliacionService) para distinguir errores de conexión de errores de consulta.</summary>
    private static EstadoConciliacionDomicilio ClasificarError(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            int[] erroresDeConexion = { -2, -1, 2, 53, 233, 4060, 10060, 11001, 18456, 40613 };
            return erroresDeConexion.Contains(sqlEx.Number)
                ? EstadoConciliacionDomicilio.ERROR_CONEXION
                : EstadoConciliacionDomicilio.ERROR_SQL;
        }

        return ex is TimeoutException ? EstadoConciliacionDomicilio.ERROR_CONEXION : EstadoConciliacionDomicilio.ERROR_SQL;
    }

    public EstadoConciliacionDomicilioDto ObtenerEstado()
    {
        lock (_lock)
        {
            return new EstadoConciliacionDomicilioDto
            {
                Estado = _estado,
                FechaInicio = _fechaInicioActual,
                FechaFin = _fechaFinActual,
                LocalesTotal = _localesTotal,
                LocalesProcesados = _localesProcesados,
                SolicitudesTotal = _solicitudesTotal,
                SolicitudesProcesadas = _solicitudesProcesadas,
                LocalesEnProceso = _localesEnProceso.ToList(),
                IniciadoEn = _iniciadoEn,
                FinalizadoEn = _finalizadoEn,
                Resumen = _resumen
            };
        }
    }

    private void MarcarLocalEnProceso(string local)
    {
        var clave = string.IsNullOrWhiteSpace(local) ? "DESCONOCIDO" : local;
        lock (_lock)
        {
            _localesEnProceso.Add(clave);
        }
    }

    private void ActualizarProgresoLocal(string local, int cantidad)
    {
        var clave = string.IsNullOrWhiteSpace(local) ? "DESCONOCIDO" : local;
        lock (_lock)
        {
            _localesEnProceso.Remove(clave);
            _localesProcesados++;
            _solicitudesProcesadas += cantidad;
        }
    }

    private static ConciliacionDomicilioResumen ConstruirResumen(DateOnly fechaInicio, DateOnly fechaFin, int localesProcesados, List<ConciliacionDomicilioResult> resultados)
    {
        return new ConciliacionDomicilioResumen
        {
            FechaInicio = fechaInicio.ToString("yyyy-MM-dd"),
            FechaFin = fechaFin.ToString("yyyy-MM-dd"),
            TotalMongo = resultados.Count,
            Conciliados = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.CONCILIADO),
            OrdenesSinFactura = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.ORDEN_SIN_FACTURA),
            Diferencias = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.DIFERENCIA_MONTO),
            FormaPagoIncorrecta = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.FORMA_PAGO_INCORRECTA),
            FacturaNoEntregada = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.FACTURA_NO_ENTREGADA),
            FacturaConPagoCancelado = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.FACTURA_CON_PAGO_CANCELADO),
            SinNovedad = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.SIN_NOVEDAD),
            ConfiguracionNoEncontrada = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.CONFIGURACION_NO_ENCONTRADA),
            ErroresConexion = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.ERROR_CONEXION),
            ErroresSql = resultados.Count(r => r.Estado == EstadoConciliacionDomicilio.ERROR_SQL),
            LocalesProcesados = localesProcesados
        };
    }
}