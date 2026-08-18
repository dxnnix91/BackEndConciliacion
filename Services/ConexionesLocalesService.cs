using Backend.Configuration;
using Backend.Models;
using Backend.Services.Interfaces;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Backend.Services;

/// <summary>
/// Reemplaza a Azure (RestauranteCentralService/AzureCentralSettings) y a la generación
/// dinámica de IP/BASE (ServidorService/LocalesSettings): implementa IServidorService e
/// IRestauranteCentralService leyendo un único documento por local desde la colección Mongo
/// "connections" (ver ConexionesLocalesSettings), en vez de golpear una base SQL Azure aparte.
///
/// Registrado como singleton y con caché en memoria (sección "agilizar el proceso"): esta es
/// una colección de configuración pequeña que casi no cambia, así que se carga completa a
/// memoria y se refresca cada ConexionesLocalesSettings.CacheMinutos en vez de hacer un
/// round-trip a Mongo por cada local en cada conciliación.
///
/// NOTA: username/password del documento vienen encriptados con el esquema de Laravel y, por
/// decisión explícita, no se desencriptan ni se usan aquí: la conexión SQL sigue usando las
/// credenciales compartidas de SqlServerSettings para todos los locales.
/// </summary>
public class ConexionesLocalesService : IServidorService, IRestauranteCentralService
{
    private readonly IMongoCollection<ConexionLocalDocument> _collection;
    private readonly ConexionesLocalesSettings _settings;
    private readonly ILogger<ConexionesLocalesService> _logger;

    private readonly SemaphoreSlim _semaforoRefresco = new(1, 1);
    private IReadOnlyList<ConexionLocalDocument> _cache = Array.Empty<ConexionLocalDocument>();
    private Dictionary<string, ConexionLocalDocument> _cachePorLocal = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, ConexionLocalDocument> _cachePorRestauranteId = new();
    private DateTime? _ultimoRefrescoUtc;

    public ConexionesLocalesService(
        IMongoClient mongoClient,
        IOptions<ConexionesLocalesSettings> settings,
        ILogger<ConexionesLocalesService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var database = mongoClient.GetDatabase(_settings.Database);
        _collection = database.GetCollection<ConexionLocalDocument>(_settings.Collection);
    }

    public async Task<IReadOnlyList<ServerConfig>> ObtenerServidoresAsync(CancellationToken cancellationToken = default)
    {
        await AsegurarCacheVigenteAsync(cancellationToken);

        return _cache
            .OrderBy(d => d.TiendaName, StringComparer.OrdinalIgnoreCase)
            .Select(MapearServerConfig)
            .ToList();
    }

    public async Task<ServerConfig?> BuscarPorLocalAsync(string local, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(local))
        {
            return null;
        }

        await AsegurarCacheVigenteAsync(cancellationToken);

        if (_cachePorLocal.TryGetValue(local.Trim(), out var documento))
        {
            return MapearServerConfig(documento);
        }

        _logger.LogWarning("No se encontró ningún documento en '{Coleccion}' con tiendaName='{Local}'", _settings.Collection, local);
        return null;
    }

    public async Task<string?> ObtenerCodigoTiendaPorRstIdAsync(int rstId, CancellationToken cancellationToken = default)
    {
        await AsegurarCacheVigenteAsync(cancellationToken);

        if (_cachePorRestauranteId.TryGetValue(rstId, out var documento))
        {
            return documento.TiendaName;
        }

        _logger.LogWarning("No se encontró ningún documento en '{Coleccion}' con restauranteId={RstId}", _settings.Collection, rstId);
        return null;
    }

    /// <summary>
    /// Refresca el caché desde Mongo sin importar cuánto tiempo lleve cargado. Expuesto para
    /// poder forzar un refresco manual (ej. tras dar de alta un local nuevo) sin reiniciar el
    /// backend, sin necesidad de esperar a que venza ConexionesLocalesSettings.CacheMinutos.
    /// </summary>
    public Task RefrescarCacheAsync(CancellationToken cancellationToken = default)
        => RefrescarCacheInternoAsync(cancellationToken);

    private async Task AsegurarCacheVigenteAsync(CancellationToken cancellationToken)
    {
        var vencido = _ultimoRefrescoUtc is null
            || DateTime.UtcNow - _ultimoRefrescoUtc.Value > TimeSpan.FromMinutes(Math.Max(1, _settings.CacheMinutos));

        if (vencido)
        {
            await RefrescarCacheInternoAsync(cancellationToken);
        }
    }

    private async Task RefrescarCacheInternoAsync(CancellationToken cancellationToken)
    {
        await _semaforoRefresco.WaitAsync(cancellationToken);
        try
        {
            // Otra llamada concurrente pudo haber refrescado el caché mientras esperábamos el
            // semáforo; no repetir el round-trip a Mongo si ya se hizo hace un instante.
            if (_ultimoRefrescoUtc is not null
                && DateTime.UtcNow - _ultimoRefrescoUtc.Value <= TimeSpan.FromMinutes(Math.Max(1, _settings.CacheMinutos)))
            {
                return;
            }

            var documentos = await _collection.Find(FilterDefinition<ConexionLocalDocument>.Empty)
                .ToListAsync(cancellationToken);

            var porLocal = new Dictionary<string, ConexionLocalDocument>(StringComparer.OrdinalIgnoreCase);
            var porRestauranteId = new Dictionary<int, ConexionLocalDocument>();

            foreach (var documento in documentos)
            {
                if (!string.IsNullOrWhiteSpace(documento.TiendaName))
                {
                    porLocal[documento.TiendaName.Trim()] = documento;
                }

                if (documento.RestauranteId != 0)
                {
                    porRestauranteId[documento.RestauranteId] = documento;
                }
            }

            _cache = documentos;
            _cachePorLocal = porLocal;
            _cachePorRestauranteId = porRestauranteId;
            _ultimoRefrescoUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Caché de conexiones de locales refrescado desde '{Base}.{Coleccion}': {Cantidad} documentos",
                _settings.Database, _settings.Collection, documentos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudo refrescar el caché de conexiones de locales desde '{Base}.{Coleccion}'. Se sigue usando el caché anterior ({Cantidad} documentos) si existe.",
                _settings.Database, _settings.Collection, _cache.Count);

            // Si nunca hubo un refresco exitoso, no hay caché previo que conservar: se propaga
            // el error en vez de fingir que hay datos.
            if (_ultimoRefrescoUtc is null)
            {
                throw;
            }
        }
        finally
        {
            _semaforoRefresco.Release();
        }
    }

    /// <summary>
    /// La base de datos SQL Server sigue el patrón fijo "MAXPOINT_" + tiendaName (ej. "K039" ->
    /// "MAXPOINT_K039"). El campo "databaseName" del documento de Mongo NO es confiable para
    /// esto (a veces trae valores incorrectos, ej. "MAXPOINT_K043_DT" en vez de
    /// "MAXPOINT_K043"), así que se ignora deliberadamente y se construye siempre a partir de
    /// tiendaName, que sí es confiable.
    /// </summary>
    private static ServerConfig MapearServerConfig(ConexionLocalDocument documento) => new()
    {
        Local = documento.TiendaName,
        Ip = documento.ServerName,
        Base = $"MAXPOINT_{documento.TiendaName}",
        Puerto = documento.Port,
        InstanceName = documento.InstanceName
    };
}