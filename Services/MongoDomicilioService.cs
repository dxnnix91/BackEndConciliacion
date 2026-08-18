using Backend.Configuration;
using Backend.Models;
using Backend.Services.Interfaces;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Backend.Services;

/// <inheritdoc cref="IMongoDomicilioService"/>
/// <summary>
/// A diferencia de MongoService (kiosko), aquí se traen TODAS las solicitudes del rango de
/// fechas sin filtrar por status: se necesitan también los estados Cancel/Refunded/etc. para
/// poder detectar facturas generadas en SQL Server sobre pagos que en Deuna quedaron
/// cancelados o reembolsados.
/// </summary>
public class MongoDomicilioService : IMongoDomicilioService
{
    private readonly IMongoCollection<RequestPaymentDocument> _collection;
    private readonly IRestauranteCentralService _restauranteCentralService;
    private readonly ILogger<MongoDomicilioService> _logger;

    // Mismo ajuste de zona horaria que MongoService: createdAt está en UTC, pero el día
    // calendario relevante para conciliar es el de Ecuador (UTC-5 fijo, sin horario de verano).
    private static readonly TimeSpan OffsetEcuador = TimeSpan.FromHours(-5);

    public MongoDomicilioService(
        IMongoClient client,
        IOptions<DomicilioSettings> settings,
        IRestauranteCentralService restauranteCentralService,
        ILogger<MongoDomicilioService> logger)
    {
        _restauranteCentralService = restauranteCentralService;
        _logger = logger;

        var domicilioSettings = settings.Value;
        var database = client.GetDatabase(domicilioSettings.Database);
        _collection = database.GetCollection<RequestPaymentDocument>(domicilioSettings.Collection);
    }

    public async Task<List<RequestPaymentDocument>> ObtenerSolicitudesAsync(DateOnly fechaInicio, DateOnly fechaFin, CancellationToken cancellationToken = default)
    {
        var inicio = new DateTimeOffset(fechaInicio.ToDateTime(TimeOnly.MinValue), OffsetEcuador).UtcDateTime;
        var finExclusivo = new DateTimeOffset(fechaFin.ToDateTime(TimeOnly.MinValue), OffsetEcuador).UtcDateTime.AddDays(1);

        _logger.LogInformation(
            "Consultando MongoDB (domicilio): createdAt >= {Inicio} y < {Fin}, todos los status",
            inicio, finExclusivo);

        var filtro = Builders<RequestPaymentDocument>.Filter.And(
            Builders<RequestPaymentDocument>.Filter.Gte(p => p.CreatedAt, inicio),
            Builders<RequestPaymentDocument>.Filter.Lt(p => p.CreatedAt, finExclusivo));

        var solicitudes = await _collection.Find(filtro).ToListAsync(cancellationToken);

        var cacheRestauranteIdALocal = new Dictionary<int, string?>();

        foreach (var solicitud in solicitudes)
        {
            solicitud.Local = ExtraerLocal(solicitud.RequestId);

            if (EsLocalValido(solicitud.Local))
            {
                continue;
            }

            // Respaldo igual al de kiosko (branchOffice/rst_id), pero aquí la llave es
            // store.id: se busca en la colección "connections" el tiendaName correspondiente.
            var restauranteId = solicitud.Store?.Id ?? 0;
            if (restauranteId == 0)
            {
                continue;
            }

            if (!cacheRestauranteIdALocal.TryGetValue(restauranteId, out var local))
            {
                local = await _restauranteCentralService.ObtenerCodigoTiendaPorRstIdAsync(restauranteId, cancellationToken);
                cacheRestauranteIdALocal[restauranteId] = local;
            }

            if (!string.IsNullOrWhiteSpace(local))
            {
                _logger.LogInformation(
                    "Local recuperado vía store.id={RestauranteId} -> {Local} para requestId='{RequestId}'",
                    restauranteId, local, solicitud.RequestId);
                solicitud.Local = local;
            }
        }

        _logger.LogInformation(
            "MongoDB (domicilio) devolvió {Cantidad} solicitudes entre {FechaInicio} y {FechaFin}",
            solicitudes.Count, fechaInicio, fechaFin);

        return solicitudes;
    }

    /// <summary>Extrae el local desde requestId, ej. "DEUNA-K028-13291-1786927680" -> "K028".</summary>
    private static string ExtraerLocal(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return string.Empty;
        }

        var partes = requestId.Split('-');
        return partes.Length >= 2 ? partes[1].Trim() : string.Empty;
    }

    private static bool EsLocalValido(string local)
        => !string.IsNullOrWhiteSpace(local) && local.Any(char.IsDigit);
}