namespace Backend.Configuration;

/// <summary>
/// Ubicación del CSV que define los servidores SQL Server (IP, BASE).
/// Ejemplo: Csv__Path = Data/servidores.csv
/// </summary>
public class CsvSettings
{
    public const string SectionName = "Csv";

    public string Path { get; set; } = "Data/servidores.csv";
}