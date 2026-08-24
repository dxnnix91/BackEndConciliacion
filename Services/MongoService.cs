using Backend.Configuration;
using Backend.Models;
using Backend.Services.Interfaces;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Backend.Services;

/// <inheritdoc cref="IMongoService"/>
public class MongoService : IMongoService
{
    private readonly IMongoCollection<MongoPayment> _collection;
    private readonly IRestauranteCentralService _restauranteCentralService;
    private readonly ILogger<MongoService> _logger;

    /// <summary>
    /// Mongo guarda createdAt en UTC, pero fe_fechaOperacion en SQL Server representa el día
    /// calendario de Ecuador (America/Guayaquil, UTC-5 fijo todo el año, sin horario de verano).
    /// Si no se corrige este desfase, pedir "12 de agosto" arma en Mongo la ventana
    /// [12 ago 00:00 UTC, 13 ago 00:00 UTC) = [11 ago 19:00 Ecuador, 12 ago 19:00 Ecuador),
    /// que NO es el mismo día calendario que usa SQL Server — por eso transacciones de la
    /// noche del día anterior (hora Ecuador) aparecían como "Falta en SQL": SQL nunca las
    /// tenía en su rango, porque para SQL esas horas ya eran del día anterior.
    /// </summary>
    private static readonly TimeSpan OffsetEcuador = TimeSpan.FromHours(-5);

    public MongoService(
        IMongoClient client,
        IOptions<MongoSettings> settings,
        IRestauranteCentralService restauranteCentralService,
        ILogger<MongoService> logger)
    {
        _restauranteCentralService = restauranteCentralService;
        _logger = logger;

        var mongoSettings = settings.Value;
        var database = client.GetDatabase(mongoSettings.Database);
        _collection = database.GetCollection<MongoPayment>(mongoSettings.Collection);
    }

    public Task<List<MongoPayment>> ObtenerTransaccionesAprobadasAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default)
        => ObtenerTransaccionesPorStatusAsync("approved", fechaInicio, fechaFin, cancellationToken);

    /// <summary>
    /// Igual que ObtenerTransaccionesAprobadasAsync pero filtrando por "cancelled" en vez de
    /// "approved" (sección nueva: detectar pagos cancelados en Mongo que sí se facturaron y
    /// entregaron en SQL Server). Comparte toda la lógica de rango de fechas y de respaldo de
    /// local por branchOffice con el método de aprobados.
    /// </summary>
    public Task<List<MongoPayment>> ObtenerTransaccionesCanceladasAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default)
        => ObtenerTransaccionesPorStatusAsync("cancelled", fechaInicio, fechaFin, cancellationToken);

    private async Task<List<MongoPayment>> ObtenerTransaccionesPorStatusAsync(string status, DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default)
    {
        // Rango [inicio de fechaInicio, inicio del día siguiente a fechaFin) según el día
        // calendario de Ecuador, convertido a UTC para poder comparar contra createdAt
        // (sección 3), extendido a un rango de varios días en vez de un solo día.
        // NUNCA usar 23:59:59.999 como límite superior.
        var inicio = new DateTimeOffset(fechaInicio.ToDateTime(TimeOnly.MinValue), OffsetEcuador).UtcDateTime;
        var finExclusivo = new DateTimeOffset(fechaFin.ToDateTime(TimeOnly.MinValue), OffsetEcuador).UtcDateTime.AddDays(1);

        _logger.LogInformation(
            "Consultando MongoDB: status={Status}, createdAt >= {Inicio} y < {Fin}",
            status, inicio, finExclusivo);

        var filtro = Builders<MongoPayment>.Filter.And(
            Builders<MongoPayment>.Filter.Eq(p => p.Status, status),
            Builders<MongoPayment>.Filter.Gte(p => p.CreatedAt, inicio),
            Builders<MongoPayment>.Filter.Lt(p => p.CreatedAt, finExclusivo));

        var transacciones = await _collection.Find(filtro).ToListAsync(cancellationToken);

        foreach (var transaccion in transacciones)
        {
            transaccion.Local = ExtraerLocal(transaccion.ExternalReference);
        }

        // Respaldo (sección nueva): cuando el externalReference viene incompleto o corrupto
        // (ej. "EC--34-..." con el local vacío, o "EC-null-32-..." con el local literalmente
        // como el texto "null"), no se puede determinar la tienda desde ahí. En esos casos se
        // usa metadataCreatePayment.branchOffice, que corresponde exactamente al restauranteId
        // en la colección Mongo "connections", y se consulta ahí (ya no Azure/MAXPOINT) para
        // recuperar el tiendaName real (ej. "K172"). Con eso la transacción se agrupa
        // normalmente con el resto de su tienda en el resto del flujo, en vez de caer directo a
        // CONFIGURACION_NO_ENCONTRADA.
        var cacheRstIdACodigoTienda = new Dictionary<int, string?>();

        foreach (var transaccion in transacciones)
        {
            if (EsLocalValido(transaccion.Local))
            {
                continue;
            }

            var branchOffice = transaccion.MetadataCreatePayment?.BranchOffice;
            if (string.IsNullOrWhiteSpace(branchOffice) || !int.TryParse(branchOffice, out var rstId))
            {
                continue;
            }

            if (!cacheRstIdACodigoTienda.TryGetValue(rstId, out var codigoTienda))
            {
                codigoTienda = await _restauranteCentralService.ObtenerCodigoTiendaPorRstIdAsync(rstId, cancellationToken);
                cacheRstIdACodigoTienda[rstId] = codigoTienda;
            }

            if (!string.IsNullOrWhiteSpace(codigoTienda))
            {
                _logger.LogInformation(
                    "Local recuperado vía branchOffice/restauranteId={RstId} -> {CodigoTienda} para externalReference='{ExternalReference}' (venía como local='{LocalOriginal}')",
                    rstId, codigoTienda, transaccion.ExternalReference, transaccion.Local);
                transaccion.Local = codigoTienda;
            }
        }

        _logger.LogInformation(
            "MongoDB devolvió {Cantidad} transacciones con status={Status} entre {FechaInicio} y {FechaFin}",
            transacciones.Count, status, fechaInicio, fechaFin);

        return transacciones;
    }

    /// <summary>
    /// Extrae el código de local desde externalReference, ej. "EC-K004-33-1785542943" -> "K004".
    /// Formato asumido: EC-{local}-{kiosko}-{timestamp}, en posiciones fijas.
    /// IMPORTANTE: no usar StringSplitOptions.RemoveEmptyEntries aquí. Si el segmento del local
    /// viene vacío (ej. "EC--31-1786060779"), RemoveEmptyEntries lo elimina y desplaza los demás
    /// segmentos, haciendo que se tome por error el kiosko ("31") como si fuera el local.
    /// Con Split simple se respetan las posiciones: un local vacío se detecta como vacío de
    /// verdad y la transacción cae correctamente en CONFIGURACION_NO_ENCONTRADA / "DESCONOCIDO",
    /// en vez de asignarse a un local incorrecto.
    /// </summary>
    private static string ExtraerLocal(string externalReference)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            return string.Empty;
        }

        var partes = externalReference.Split('-');
        return partes.Length >= 2 ? partes[1].Trim() : string.Empty;
    }

    /// <summary>
    /// Un local es válido si trae al menos un dígito (ej. "K172"). Casos como "" (vacío) o el
    /// texto literal "null" no tienen ningún dígito y se consideran inválidos, disparando el
    /// respaldo por branchOffice.
    /// </summary>
    private static bool EsLocalValido(string local)
        => !string.IsNullOrWhiteSpace(local) && local.Any(char.IsDigit);
}