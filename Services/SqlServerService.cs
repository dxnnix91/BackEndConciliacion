using Backend.Configuration;
using Backend.Models;
using Backend.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <inheritdoc cref="ISqlServerService"/>
public class SqlServerService : ISqlServerService
{
    private readonly SqlServerSettings _settings;
    private readonly ILogger<SqlServerService> _logger;

    // ============================================================================================
    // IMPORTANTE (sección 6 del requerimiento): esta es la consulta SQL EXISTENTE y VALIDADA.
    // Los JOINs, los nombres de tablas y las demás condiciones (fp.fmp_descripcion = 'DE UNA')
    // NO se tocan.
    // CAMBIO EXPLÍCITO solicitado por el usuario (para agilizar la consulta): en vez de filtrar
    // por rango de fechas, se filtra directamente por la lista de codigo_app que ya vienen de
    // Mongo para ese local (kcp.codigo_app IN (...)), ya que esa es la llave real de
    // conciliación. Esto evita además el CONVERT(date, ...) sobre fe_fechaOperacion, que
    // impedía usar cualquier índice sobre esa columna.
    // Efecto colateral aceptado explícitamente por el usuario: ya no se puede detectar
    // FALTA_MONGO (pagos que existen en SQL pero nunca llegaron a Mongo), porque solo se
    // consultan los códigos que Mongo ya entregó.
    // ============================================================================================
    // NOTA: "cfac_id" aparece con el mismo nombre en kiosko_cabecera_pedidos, Cabecera_Factura y
    // Formapago_Factura, y con SELECT * eso genera columnas duplicadas por nombre. El cfac_id que
    // realmente representa la factura es el de Cabecera_Factura (cf), así que se agrega un alias
    // explícito adicional (cfac_id_factura) al final del SELECT *, SIN quitar ni tocar ningún JOIN
    // existente, para poder leerlo sin ambigüedad en el código C#.
    private const string ConsultaBaseSinFiltroCodigos = @"
        SELECT *, cf.cfac_id AS cfac_id_factura
        FROM kiosko_cabecera_pedidos AS kcp
        INNER JOIN Cabecera_Factura AS cf
            ON cf.cfac_id = kcp.cfac_id
        INNER JOIN Formapago_Factura AS fpf
            ON fpf.cfac_id = cf.cfac_id
        INNER JOIN Formapago AS fp
            ON fp.IDFormapago = fpf.IDFormapago
        INNER JOIN Restaurante AS r
            ON r.rst_id = cf.rst_id
        WHERE fp.fmp_descripcion = 'DE UNA'
        AND kcp.codigo_app IN ({0})";

    // SQL Server permite hasta ~2100 parámetros por consulta; se deja margen y se parte en
    // lotes si un local tiene más códigos que esto en un mismo rango de fechas.
    private const int MaxCodigosPorConsulta = 2000;

    public SqlServerService(IOptions<SqlServerSettings> settings, ILogger<SqlServerService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<SqlPayment>> ObtenerPagosPorCodigosAsync(ServerConfig servidor, IReadOnlyList<string> codigosApp, CancellationToken cancellationToken = default)
    {
        var resultado = new List<SqlPayment>();

        var codigosValidos = codigosApp.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        if (codigosValidos.Count == 0)
        {
            return resultado;
        }

        // Una sola conexión por servidor/local (sección 11): si hay demasiados códigos para un
        // solo IN (...), se reutiliza la misma conexión para los lotes siguientes en vez de
        // abrir una conexión nueva por lote.
        await using var connection = new SqlConnection(ConstruirConnectionString(servidor));
        await connection.OpenAsync(cancellationToken);

        foreach (var lote in Chunk(codigosValidos, MaxCodigosPorConsulta))
        {
            var nombresParametros = new string[lote.Count];

            await using var command = connection.CreateCommand();
            command.CommandTimeout = _settings.CommandTimeoutSeconds;

            for (var i = 0; i < lote.Count; i++)
            {
                var nombreParametro = $"@cod{i}";
                nombresParametros[i] = nombreParametro;
                command.Parameters.Add(new SqlParameter(nombreParametro, System.Data.SqlDbType.NVarChar, 200)
                {
                    Value = lote[i]
                });
            }

            command.CommandText = string.Format(ConsultaBaseSinFiltroCodigos, string.Join(", ", nombresParametros));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var ordCodigoApp = ObtenerOrdinalSeguro(reader, "codigo_app");
            var ordRstId = ObtenerOrdinalSeguro(reader, "rst_id");
            var ordCfacId = ObtenerOrdinalSeguro(reader, "cfac_id_factura");
            var ordFecha = ObtenerOrdinalSeguro(reader, "fe_fechaOperacion");
            var ordCfacTotal = ObtenerOrdinalSeguro(reader, "cfac_total");
            var ordFpfTotalPagar = ObtenerOrdinalSeguro(reader, "fpf_total_pagar");
            var ordFmpDescripcion = ObtenerOrdinalSeguro(reader, "fmp_descripcion");

            if (ordCfacId < 0)
            {
                // Si esto se registra, el alias "cfac_id_factura" no llegó en el resultado (por
                // ejemplo, si el driver no soporta un alias con guion bajo, cosa rara, o si la
                // consulta no se actualizó realmente en el servidor). Sirve para diagnosticar
                // rápido si el problema persiste después de este cambio.
                _logger.LogWarning(
                    "Servidor {Local} ({Ip}/{Base}): no se encontró la columna 'cfac_id_factura' en el resultado. Verifique que la consulta actualizada esté desplegada.",
                    servidor.Local, servidor.Ip, servidor.Base);
            }

            while (await reader.ReadAsync(cancellationToken))
            {
                try
                {
                    resultado.Add(new SqlPayment
                    {
                        CodigoApp = LeerString(reader, ordCodigoApp),
                        RstId = LeerInt(reader, ordRstId, "rst_id"),
                        CfacId = LeerString(reader, ordCfacId),
                        FechaOperacion = LeerFecha(reader, ordFecha, "fe_fechaOperacion"),
                        CfacTotal = LeerDecimal(reader, ordCfacTotal, "cfac_total"),
                        FpfTotalPagar = LeerDecimal(reader, ordFpfTotalPagar, "fpf_total_pagar"),
                        FormaPagoDescripcion = LeerString(reader, ordFmpDescripcion)
                    });
                }
                catch (Exception ex)
                {
                    // Una sola fila con un valor inesperado (ej. SELECT * trae una columna
                    // duplicada por nombre entre las tablas del JOIN, y termina leyéndose el
                    // valor equivocado) NO debe tirar abajo todo el lote de este servidor: se
                    // descarta esa fila puntual, se deja registrado con el codigo_app para poder
                    // rastrearla, y se sigue leyendo el resto sin perder la conciliación de las
                    // demás transacciones de este local.
                    _logger.LogWarning(ex,
                        "Servidor {Local} ({Ip}/{Base}): se descartó una fila por un valor con formato inesperado (codigo_app='{CodigoApp}')",
                        servidor.Local, servidor.Ip, servidor.Base, LeerString(reader, ordCodigoApp));
                }
            }
        }

        _logger.LogInformation(
            "Servidor {Local} ({Ip}/{Base}): {Encontrados} de {Solicitados} códigos encontrados",
            servidor.Local, servidor.Ip, servidor.Base, resultado.Count, codigosValidos.Count);

        return resultado;
    }

    private static IEnumerable<List<string>> Chunk(List<string> valores, int tamanoLote)
    {
        for (var i = 0; i < valores.Count; i += tamanoLote)
        {
            yield return valores.GetRange(i, Math.Min(tamanoLote, valores.Count - i));
        }
    }

    public async Task<bool> ProbarConexionAsync(ServerConfig servidor, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(ConstruirConnectionString(servidor));
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar al servidor {Local} ({Ip}/{Base})", servidor.Local, servidor.Ip, servidor.Base);
            return false;
        }
    }

    private string ConstruirConnectionString(ServerConfig servidor)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = servidor.Ip,
            InitialCatalog = servidor.Base,
            UserID = _settings.Username,
            Password = _settings.Password,
            ConnectTimeout = _settings.ConnectTimeoutSeconds,
            TrustServerCertificate = true,
            Encrypt = true
        };

        return builder.ConnectionString;
    }

    private static int ObtenerOrdinalSeguro(SqlDataReader reader, string columna)
    {
        try
        {
            return reader.GetOrdinal(columna);
        }
        catch (IndexOutOfRangeException)
        {
            // La consulta base usa SELECT *; si el esquema real no trae esta columna con este
            // nombre exacto, se registra -1 y ese campo queda vacío en vez de romper todo el
            // proceso del servidor.
            return -1;
        }
    }

    private static string LeerString(SqlDataReader reader, int ordinal)
        => ordinal >= 0 && !reader.IsDBNull(ordinal) ? reader.GetValue(ordinal).ToString() ?? string.Empty : string.Empty;

    /// <summary>
    /// Convierte de forma segura: si el valor recibido no se puede convertir (ej. porque
    /// SELECT * trajo una columna con este mismo nombre desde otra tabla del JOIN y el valor
    /// real es texto, no un número), se registra el nombre de columna Y el valor crudo para
    /// poder diagnosticar exactamente qué columna está chocando, y se devuelve 0 en vez de
    /// tirar abajo la fila completa.
    /// </summary>
    private int LeerInt(SqlDataReader reader, int ordinal, string nombreColumna)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return 0;
        }

        var valor = reader.GetValue(ordinal);
        try
        {
            return Convert.ToInt32(valor);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            _logger.LogWarning(
                "No se pudo convertir la columna '{Columna}' a entero (valor recibido: '{Valor}'). Se usa 0.",
                nombreColumna, valor);
            return 0;
        }
    }

    private decimal LeerDecimal(SqlDataReader reader, int ordinal, string nombreColumna)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return 0m;
        }

        var valor = reader.GetValue(ordinal);
        try
        {
            return Convert.ToDecimal(valor);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            _logger.LogWarning(
                "No se pudo convertir la columna '{Columna}' a decimal (valor recibido: '{Valor}'). Se usa 0.",
                nombreColumna, valor);
            return 0m;
        }
    }

    private DateTime LeerFecha(SqlDataReader reader, int ordinal, string nombreColumna)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return default;
        }

        var valor = reader.GetValue(ordinal);
        try
        {
            return Convert.ToDateTime(valor);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            _logger.LogWarning(
                "No se pudo convertir la columna '{Columna}' a fecha (valor recibido: '{Valor}'). Se usa la fecha por defecto.",
                nombreColumna, valor);
            return default;
        }
    }
}