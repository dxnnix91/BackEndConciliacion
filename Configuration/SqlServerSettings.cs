namespace Backend.Configuration;

/// <summary>
/// Credenciales SQL Server compartidas por todos los servidores. NUNCA se exponen al
/// frontend. Deben venir de appsettings.json (solo para desarrollo local sin secretos
/// reales), User Secrets, o variables de entorno, por ejemplo:
///   SqlServer__Username
///   SqlServer__Password
///   SqlServer__ConnectTimeoutSeconds
/// </summary>
public class SqlServerSettings
{
    public const string SectionName = "SqlServer";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Cuántos locales se procesan en paralelo como máximo durante una conciliación.
    /// Cada local abre su propia conexión SQL independiente; este límite evita abrir
    /// decenas/cientos de conexiones simultáneas si hay muchos locales con transacciones
    /// en el rango de fechas. Un local lento o caído nunca bloquea a los demás: cada uno
    /// se procesa en su propia tarea con su propio try/catch.
    /// </summary>
    public int MaxGradoParalelismo { get; set; } = 8;
}