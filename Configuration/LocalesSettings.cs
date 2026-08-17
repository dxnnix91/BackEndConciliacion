namespace Backend.Configuration;

/// <summary>
/// Configuración para generar dinámicamente la conexión (IP + BASE) de cada local,
/// sin depender de un archivo CSV. Patrón observado:
///   Local "K004" -> IP "10.101.4.20", BASE "MAXPOINT_K004"
/// El mismo número de local (sin ceros a la izquierda) va en el tercer octeto de la IP,
/// y con ceros a la izquierda (3 dígitos) al final del nombre de la base de datos.
/// </summary>
public class LocalesSettings
{
    public const string SectionName = "Locales";

    /// <summary>
    /// Patrón de IP. {0} se reemplaza por el número de local sin ceros a la izquierda
    /// (ej. "10.101.{0}.20" con número 4 -> "10.101.4.20").
    /// </summary>
    public string IpPattern { get; set; } = "10.101.{0}.20";

    /// <summary>
    /// Patrón del nombre de la base de datos. {0:D3} se reemplaza por el número de local
    /// con 3 dígitos (ej. "MAXPOINT_K{0:D3}" con número 4 -> "MAXPOINT_K004").
    /// </summary>
    public string BasePattern { get; set; } = "MAXPOINT_K{0:D3}";

    /// <summary>
    /// Números de local válidos/existentes (sin el prefijo "K" ni ceros a la izquierda,
    /// ej. 1, 2, 21, 180). Se usa para: (1) no intentar conectarse a una IP que no existe
    /// si llega un número por error desde Mongo, y (2) poder listar todos los servidores
    /// en GET /servidores. Se actualiza aquí (appsettings) cuando se abre/cierra un local
    /// físico, sin tocar código ni archivos externos.
    /// </summary>
    public List<int> Codigos { get; set; } = new();
}