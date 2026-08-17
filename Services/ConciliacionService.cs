using System.Collections.Concurrent;
using Backend.Configuration;
using Backend.DTOs;
using Backend.Models;
using Backend.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <summary>
/// Orquesta el flujo completo de conciliación (sección 10). Registrado como singleton para
/// poder mantener el estado de "conciliación en curso" y el progreso, consultable desde
/// GET /api/conciliacion/estado mientras POST /api/conciliacion/ejecutar sigue corriendo.
/// </summary>
public class ConciliacionService : IConciliacionService
{
    private readonly IMongoService _mongoService;
    private readonly ISqlServerService _sqlServerService;
    private readonly IServidorService _servidorService;
    private readonly SqlServerSettings _sqlServerSettings;
    private readonly ILogger<ConciliacionService> _logger;

    private readonly object _lock = new();
    private bool _ejecutando;
    private EstadoEjecucion _estado = EstadoEjecucion.INACTIVO;
    private string? _fechaInicioActual;
    private string? _fechaFinActual;
    private int _localesTotal;
    private int _localesProcesados;
    private int _transaccionesTotal;
    private int _transaccionesProcesadas;

    /// <summary>
    /// Locales que se están procesando ahora mismo. Como cada local corre en su propia
    /// tarea en paralelo (sección "no debe bloquear a otra"), puede haber varios al mismo
    /// tiempo; por eso es un conjunto y no un solo string.
    /// </summary>
    private readonly HashSet<string> _localesEnProceso = new();

    private DateTime? _iniciadoEn;
    private DateTime? _finalizadoEn;
    private ConciliacionResumen? _resumen;

    // Umbral de tolerancia para considerar dos montos como iguales (redondeos de centavos).
    private const decimal ToleranciaMonto = 0.01m;

    public ConciliacionService(
        IMongoService mongoService,
        ISqlServerService sqlServerService,
        IServidorService servidorService,
        IOptions<SqlServerSettings> sqlServerSettings,
        ILogger<ConciliacionService> logger)
    {
        _mongoService = mongoService;
        _sqlServerService = sqlServerService;
        _servidorService = servidorService;
        _sqlServerSettings = sqlServerSettings.Value;
        _logger = logger;
    }

    public async Task<ConciliacionResponse> EjecutarAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default)
    {
        if (fechaFin < fechaInicio)
        {
            throw new ArgumentException("La fecha de fin no puede ser anterior a la fecha de inicio.");
        }

        lock (_lock)
        {
            if (_ejecutando)
            {
                throw new InvalidOperationException("Ya existe una conciliación en ejecución.");
            }

            _ejecutando = true;
            _estado = EstadoEjecucion.EN_PROGRESO;
            _fechaInicioActual = fechaInicio.ToString("yyyy-MM-dd");
            _fechaFinActual = fechaFin.ToString("yyyy-MM-dd");
            _localesTotal = 0;
            _localesProcesados = 0;
            _transaccionesTotal = 0;
            _transaccionesProcesadas = 0;
            _localesEnProceso.Clear();
            _iniciadoEn = DateTime.UtcNow;
            _finalizadoEn = null;
            _resumen = null;
        }

        // Colecciones thread-safe: cada local se procesa en su propia tarea en paralelo,
        // así que varias tareas escriben resultados al mismo tiempo.
        var resultados = new ConcurrentQueue<ConciliacionResult>();
        var totalSqlAcumulado = 0;

        try
        {
            _logger.LogInformation("=== Inicio de conciliación entre {FechaInicio} y {FechaFin} ===", fechaInicio, fechaFin);

            var transaccionesMongo = await _mongoService.ObtenerTransaccionesAprobadasAsync(fechaInicio, fechaFin, cancellationToken);
            var grupos = transaccionesMongo.GroupBy(t => t.Local).ToList();

            lock (_lock)
            {
                _transaccionesTotal = transaccionesMongo.Count;
                _localesTotal = grupos.Count;
            }

            _logger.LogInformation("Locales encontrados en Mongo: {Cantidad}", grupos.Count);

            // Grado máximo de locales procesándose al mismo tiempo (Locales/SqlServer:MaxGradoParalelismo).
            // Cada local abre su propia conexión SQL independiente; un local lento, caído o con
            // errores NUNCA bloquea ni retrasa a los demás, porque cada uno corre en su propia
            // tarea con su propio try/catch (sección "no debe bloquear a otra").
            var maxGradoParalelismo = Math.Max(1, _sqlServerSettings.MaxGradoParalelismo);

            await Parallel.ForEachAsync(
                grupos,
                new ParallelOptions { MaxDegreeOfParallelism = maxGradoParalelismo, CancellationToken = cancellationToken },
                async (grupo, ct) => await ProcesarLocalAsync(
                    grupo.Key,
                    grupo.ToList(),
                    resultados,
                    valorSqlAcumulado => Interlocked.Add(ref totalSqlAcumulado, valorSqlAcumulado),
                    ct));

            var resultadosFinales = resultados.ToList();
            var resumen = ConstruirResumen(fechaInicio, fechaFin, transaccionesMongo.Count, totalSqlAcumulado, grupos.Count, resultadosFinales);

            lock (_lock)
            {
                _resumen = resumen;
                _estado = resumen.ErroresConexion + resumen.ErroresSql > 0
                    ? EstadoEjecucion.FINALIZADO_CON_ERRORES
                    : EstadoEjecucion.FINALIZADO;
                _finalizadoEn = DateTime.UtcNow;
            }

            _logger.LogInformation("=== Fin de conciliación entre {FechaInicio} y {FechaFin}: {@Resumen} ===", fechaInicio, fechaFin, resumen);

            return new ConciliacionResponse
            {
                Success = true,
                Resumen = resumen,
                Resultados = resultadosFinales
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado ejecutando la conciliación entre {FechaInicio} y {FechaFin}", fechaInicio, fechaFin);

            lock (_lock)
            {
                _estado = EstadoEjecucion.FINALIZADO_CON_ERRORES;
                _finalizadoEn = DateTime.UtcNow;
            }

            return new ConciliacionResponse
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
    /// Procesa un único local de principio a fin (consulta SQL, comparación en memoria contra
    /// las transacciones de Mongo de ese local, construcción de resultados). Se invoca una vez
    /// por cada grupo dentro de Parallel.ForEachAsync, así que varias instancias de este método
    /// corren al mismo tiempo para distintos locales — cualquier error o lentitud aquí queda
    /// aislado a este local gracias al try/catch interno y a que cada uno usa su propia conexión.
    /// </summary>
    private async Task ProcesarLocalAsync(
        string local,
        List<MongoPayment> transaccionesDelLocal,
        ConcurrentQueue<ConciliacionResult> resultados,
        Action<int> acumularTotalSql,
        CancellationToken cancellationToken)
    {
        MarcarLocalEnProceso(local);
        try
        {
            if (string.IsNullOrWhiteSpace(local))
            {
                _logger.LogWarning("Se encontraron {Cantidad} transacciones sin local identificable en externalReference", transaccionesDelLocal.Count);
                foreach (var resultado in transaccionesDelLocal.Select(t => ConstruirResultadoSinConfiguracion(t, local: "DESCONOCIDO")))
                {
                    resultados.Enqueue(resultado);
                }
                return;
            }

            var servidor = _servidorService.BuscarPorLocal(local);
            if (servidor is null)
            {
                _logger.LogWarning("Local {Local} no tiene configuración de servidor válida (formato de local irreconocible)", local);
                foreach (var resultado in transaccionesDelLocal.Select(t => ConstruirResultadoSinConfiguracion(t, local)))
                {
                    resultados.Enqueue(resultado);
                }
                return;
            }

            try
            {
                // Una sola conexión/consulta por local (sección 11); ya no se filtra por rango
                // de fechas sino por la lista exacta de codigo_app que traen las transacciones
                // de Mongo de este local (decisión explícita para agilizar la consulta). Esto
                // implica que ya no se detectan pagos que existen en SQL pero nunca llegaron a
                // Mongo (FALTA_MONGO): solo se pregunta por los códigos que Mongo ya entregó.
                var codigosApp = transaccionesDelLocal
                    .Select(t => t.ExternalReference)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var pagosSql = await _sqlServerService.ObtenerPagosPorCodigosAsync(servidor, codigosApp, cancellationToken);
                acumularTotalSql(pagosSql.Count);

                var pagosPorCodigo = new Dictionary<string, SqlPayment>(StringComparer.OrdinalIgnoreCase);
                foreach (var pago in pagosSql)
                {
                    if (!string.IsNullOrWhiteSpace(pago.CodigoApp) && !pagosPorCodigo.ContainsKey(pago.CodigoApp))
                    {
                        pagosPorCodigo[pago.CodigoApp] = pago;
                    }
                }

                foreach (var transaccion in transaccionesDelLocal)
                {
                    if (pagosPorCodigo.TryGetValue(transaccion.ExternalReference, out var sqlPago))
                    {
                        resultados.Enqueue(ConstruirResultadoConciliado(transaccion, sqlPago, local));
                    }
                    else
                    {
                        resultados.Enqueue(ConstruirResultadoFaltaSql(transaccion, local));
                    }
                }
            }
            catch (Exception ex)
            {
                var estadoError = ClasificarError(ex);
                _logger.LogError(ex,
                    "Error procesando local {Local} (IP={Ip}, Base={Base}): {Estado}",
                    local, servidor.Ip, servidor.Base, estadoError);

                foreach (var resultado in transaccionesDelLocal.Select(t => ConstruirResultadoError(t, local, estadoError, ex.Message)))
                {
                    resultados.Enqueue(resultado);
                }
            }
        }
        finally
        {
            ActualizarProgresoLocal(local, transaccionesDelLocal.Count);
        }
    }

    public EstadoConciliacionDto ObtenerEstado()
    {
        lock (_lock)
        {
            return new EstadoConciliacionDto
            {
                Estado = _estado,
                FechaInicio = _fechaInicioActual,
                FechaFin = _fechaFinActual,
                LocalesTotal = _localesTotal,
                LocalesProcesados = _localesProcesados,
                TransaccionesTotal = _transaccionesTotal,
                TransaccionesProcesadas = _transaccionesProcesadas,
                LocalesEnProceso = _localesEnProceso.ToList(),
                IniciadoEn = _iniciadoEn,
                FinalizadoEn = _finalizadoEn,
                Resumen = _resumen
            };
        }
    }

    public IReadOnlyList<ServidorDto> ObtenerServidores()
    {
        return _servidorService.ObtenerServidores()
            .Select(s => new ServidorDto
            {
                Local = s.Local,
                Ip = s.Ip,
                Base = s.Base,
                Estado = "SIN_VERIFICAR"
            })
            .ToList();
    }

    private void MarcarLocalEnProceso(string local)
    {
        var clave = string.IsNullOrWhiteSpace(local) ? "DESCONOCIDO" : local;
        lock (_lock)
        {
            _localesEnProceso.Add(clave);
        }
    }

    private void ActualizarProgresoLocal(string local, int transaccionesDelLocal)
    {
        var clave = string.IsNullOrWhiteSpace(local) ? "DESCONOCIDO" : local;
        lock (_lock)
        {
            _localesEnProceso.Remove(clave);
            _localesProcesados++;
            _transaccionesProcesadas += transaccionesDelLocal;
        }
    }

    private static ConciliacionResumen ConstruirResumen(
        DateOnly fechaInicio, DateOnly fechaFin, int totalMongo, int totalSql, int localesProcesados, List<ConciliacionResult> resultados)
    {
        return new ConciliacionResumen
        {
            FechaInicio = fechaInicio.ToString("yyyy-MM-dd"),
            FechaFin = fechaFin.ToString("yyyy-MM-dd"),
            TotalMongo = totalMongo,
            TotalSql = totalSql,
            Conciliados = resultados.Count(r => r.Estado == EstadoConciliacion.CONCILIADO),
            FaltantesSql = resultados.Count(r => r.Estado == EstadoConciliacion.FALTA_SQL),
            FaltantesMongo = resultados.Count(r => r.Estado == EstadoConciliacion.FALTA_MONGO),
            Diferencias = resultados.Count(r => r.Estado == EstadoConciliacion.DIFERENCIA_MONTO),
            ErroresConexion = resultados.Count(r => r.Estado == EstadoConciliacion.ERROR_CONEXION),
            ErroresSql = resultados.Count(r => r.Estado == EstadoConciliacion.ERROR_SQL),
            ConfiguracionNoEncontrada = resultados.Count(r => r.Estado == EstadoConciliacion.CONFIGURACION_NO_ENCONTRADA),
            LocalesProcesados = localesProcesados
        };
    }

    /// <summary>
    /// Convierte el monto de Mongo (almacenado en centavos, ej. 699 = $6.99) a unidades de
    /// moneda, para poder compararlo contra los montos de SQL Server.
    /// </summary>
    private static decimal? ConvertirCentavos(decimal? valorEnCentavos)
        => valorEnCentavos.HasValue ? valorEnCentavos.Value / 100m : null;

    private static ConciliacionResult ConstruirResultadoConciliado(MongoPayment mongo, SqlPayment sql, string local)
    {
        var mongoAmount = ConvertirCentavos(mongo.Amount);
        var esIgual = mongoAmount.HasValue && Math.Abs(mongoAmount.Value - sql.FpfTotalPagar) <= ToleranciaMonto;

        return new ConciliacionResult
        {
            Local = local,
            BranchOffice = mongo.MetadataCreatePayment?.BranchOffice ?? string.Empty,
            ExternalReference = mongo.ExternalReference,
            CodigoApp = sql.CodigoApp,
            Estado = esIgual ? EstadoConciliacion.CONCILIADO : EstadoConciliacion.DIFERENCIA_MONTO,
            MongoStatus = mongo.Status,
            MongoAmount = mongoAmount,
            MongoOrderDetailTotal = ConvertirCentavos(mongo.OrderDetail?.Price?.Total),
            SqlAmount = sql.CfacTotal,
            SqlFpfTotalPagar = sql.FpfTotalPagar,
            CfacId = sql.CfacId,
            FechaMongo = mongo.CreatedAt,
            FechaSql = sql.FechaOperacion,
            Mensaje = esIgual
                ? "Conciliación correcta"
                : "Referencia encontrada en ambos sistemas, pero los montos no coinciden"
        };
    }

    private static ConciliacionResult ConstruirResultadoFaltaSql(MongoPayment mongo, string local)
    {
        return new ConciliacionResult
        {
            Local = local,
            BranchOffice = mongo.MetadataCreatePayment?.BranchOffice ?? string.Empty,
            ExternalReference = mongo.ExternalReference,
            CodigoApp = null,
            Estado = EstadoConciliacion.FALTA_SQL,
            MongoStatus = mongo.Status,
            MongoAmount = ConvertirCentavos(mongo.Amount),
            MongoOrderDetailTotal = ConvertirCentavos(mongo.OrderDetail?.Price?.Total),
            FechaMongo = mongo.CreatedAt,
            Mensaje = "Pago aprobado en MongoDB pero no encontrado en SQL Server"
        };
    }

    /// <summary>
    /// Ya no se invoca: desde que la consulta SQL filtra por codigo_app IN (...) en vez de por
    /// rango de fechas, ya no se traen de SQL pagos que Mongo no haya entregado, así que este
    /// estado nunca se genera. Se deja el método por si en el futuro se agrega una auditoría
    /// aparte (por fecha) para detectar estos casos.
    /// </summary>
    private static ConciliacionResult ConstruirResultadoFaltaMongo(SqlPayment sql, string local)
    {
        return new ConciliacionResult
        {
            Local = local,
            BranchOffice = sql.RstId.ToString(),
            ExternalReference = string.Empty,
            CodigoApp = sql.CodigoApp,
            Estado = EstadoConciliacion.FALTA_MONGO,
            SqlAmount = sql.CfacTotal,
            SqlFpfTotalPagar = sql.FpfTotalPagar,
            FechaSql = sql.FechaOperacion,
            Mensaje = "Registro encontrado en SQL Server pero no encontrado en MongoDB (con status approved)"
        };
    }

    private static ConciliacionResult ConstruirResultadoSinConfiguracion(MongoPayment mongo, string local)
    {
        return new ConciliacionResult
        {
            Local = local,
            BranchOffice = mongo.MetadataCreatePayment?.BranchOffice ?? string.Empty,
            ExternalReference = mongo.ExternalReference,
            CodigoApp = null,
            Estado = EstadoConciliacion.CONFIGURACION_NO_ENCONTRADA,
            MongoStatus = mongo.Status,
            MongoAmount = ConvertirCentavos(mongo.Amount),
            FechaMongo = mongo.CreatedAt,
            Mensaje = $"No se pudo generar la conexión SQL para el local '{local}' (formato de local irreconocible)"
        };
    }

    private static ConciliacionResult ConstruirResultadoError(MongoPayment mongo, string local, EstadoConciliacion estado, string mensajeError)
    {
        return new ConciliacionResult
        {
            Local = local,
            BranchOffice = mongo.MetadataCreatePayment?.BranchOffice ?? string.Empty,
            ExternalReference = mongo.ExternalReference,
            CodigoApp = null,
            Estado = estado,
            MongoStatus = mongo.Status,
            MongoAmount = ConvertirCentavos(mongo.Amount),
            FechaMongo = mongo.CreatedAt,
            Mensaje = mensajeError
        };
    }

    /// <summary>
    /// Heurística para distinguir errores de conexión (timeout, login failed, servidor no
    /// disponible, base no encontrada) de errores propios de la consulta SQL (sección 13).
    /// </summary>
    private static EstadoConciliacion ClasificarError(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            // Números de error típicos de problemas de conexión/autenticación en SQL Server.
            int[] erroresDeConexion = { -2, -1, 2, 53, 233, 4060, 10060, 11001, 18456, 40613 };
            if (erroresDeConexion.Contains(sqlEx.Number))
            {
                return EstadoConciliacion.ERROR_CONEXION;
            }
            return EstadoConciliacion.ERROR_SQL;
        }

        if (ex is TimeoutException)
        {
            return EstadoConciliacion.ERROR_CONEXION;
        }

        return EstadoConciliacion.ERROR_SQL;
    }
}