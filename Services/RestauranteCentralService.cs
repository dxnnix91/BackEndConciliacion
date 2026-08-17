using Backend.Configuration;
using Backend.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <inheritdoc cref="IRestauranteCentralService"/>
public class RestauranteCentralService : IRestauranteCentralService
{
    private readonly AzureCentralSettings _settings;
    private readonly ILogger<RestauranteCentralService> _logger;

    private const string Consulta = "SELECT rst_cod_tienda FROM Restaurante WHERE rst_id = @rstId";

    public RestauranteCentralService(IOptions<AzureCentralSettings> settings, ILogger<RestauranteCentralService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string?> ObtenerCodigoTiendaPorRstIdAsync(int rstId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(ConstruirConnectionString());
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = Consulta;
            command.CommandTimeout = _settings.CommandTimeoutSeconds;
            command.Parameters.Add(new SqlParameter("@rstId", System.Data.SqlDbType.Int) { Value = rstId });

            var resultado = await command.ExecuteScalarAsync(cancellationToken);
            if (resultado is null || resultado is DBNull)
            {
                _logger.LogWarning("No se encontró rst_id={RstId} en la base central de Azure (Restaurante).", rstId);
                return null;
            }

            return resultado.ToString()?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando la base central de Azure para rst_id={RstId}", rstId);
            return null;
        }
    }

    private string ConstruirConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _settings.Server,
            InitialCatalog = _settings.Database,
            UserID = _settings.Username,
            Password = _settings.Password,
            ConnectTimeout = _settings.ConnectTimeoutSeconds,
            TrustServerCertificate = true,
            Encrypt = true
        };

        return builder.ConnectionString;
    }
}