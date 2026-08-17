using Backend.Configuration;
using Backend.Models;
using Backend.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <inheritdoc cref="IServidorService"/>
public class ServidorService : IServidorService
{
    private readonly LocalesSettings _settings;
    private readonly ILogger<ServidorService> _logger;

    public ServidorService(IOptions<LocalesSettings> settings, ILogger<ServidorService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public ServerConfig? BuscarPorLocal(string local)
    {
        // Totalmente dinámico: cualquier número de local genera su propia IP/BASE al vuelo
        // (ej. K014 -> 10.101.14.20 / MAXPOINT_K014, K144 -> 10.101.144.20 / MAXPOINT_K144).
        // No se valida contra ninguna lista fija: un local nuevo funciona de inmediato, sin
        // tener que mantener un arreglo de códigos ni recompilar.
        if (!TryParseNumero(local, out var numero))
        {
            _logger.LogWarning("No se pudo interpretar el número de local a partir de '{Local}'", local);
            return null;
        }

        return ConstruirServerConfig(numero);
    }

    public IReadOnlyList<ServerConfig> ObtenerServidores()
    {
        return _settings.Codigos
            .Distinct()
            .OrderBy(n => n)
            .Select(ConstruirServerConfig)
            .ToList();
    }

    private ServerConfig ConstruirServerConfig(int numero)
    {
        return new ServerConfig
        {
            Local = $"K{numero:D3}",
            Ip = string.Format(_settings.IpPattern, numero),
            Base = string.Format(_settings.BasePattern, numero)
        };
    }

    /// <summary>
    /// Extrae el número desde un código de local como "K004", "k4" o "004" -> 4.
    /// Toma únicamente los dígitos que traiga, sin importar el prefijo con el que llegue
    /// desde Mongo (ExtraerLocal en MongoService ya deja algo como "K004").
    /// </summary>
    private static bool TryParseNumero(string local, out int numero)
    {
        numero = 0;
        if (string.IsNullOrWhiteSpace(local))
        {
            return false;
        }

        var soloDigitos = new string(local.Where(char.IsDigit).ToArray());
        return !string.IsNullOrEmpty(soloDigitos) && int.TryParse(soloDigitos, out numero);
    }
}