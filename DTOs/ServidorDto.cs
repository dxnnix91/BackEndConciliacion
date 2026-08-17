namespace Backend.DTOs;

/// <summary>
/// Representación de un servidor configurado para GET /api/conciliacion/servidores.
/// Nunca incluye usuario ni contraseña (sección 25).
/// </summary>
public class ServidorDto
{
    public string Local { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Base { get; set; } = string.Empty;

    /// <summary>"OK" si se pudo abrir una conexión de prueba, "SIN_VERIFICAR" en caso contrario.</summary>
    public string Estado { get; set; } = "SIN_VERIFICAR";
}