using ClosedXML.Excel;
using Backend.Models;

namespace Backend.Helpers;

/// <summary>Genera el archivo Excel de exportación descrito en la sección 26.</summary>
public static class ExcelExportHelper
{
    public static byte[] Generar(ConciliacionResumen? resumen, List<ConciliacionResult> resultados)
    {
        using var workbook = new XLWorkbook();

        var hojaResumen = workbook.Worksheets.Add("Resumen");
        hojaResumen.Cell(1, 1).Value = "Fecha inicio";
        hojaResumen.Cell(1, 2).Value = resumen?.FechaInicio ?? string.Empty;
        hojaResumen.Cell(2, 1).Value = "Fecha fin";
        hojaResumen.Cell(2, 2).Value = resumen?.FechaFin ?? string.Empty;
        hojaResumen.Cell(3, 1).Value = "Total Mongo";
        hojaResumen.Cell(3, 2).Value = resumen?.TotalMongo ?? 0;
        hojaResumen.Cell(4, 1).Value = "Total SQL";
        hojaResumen.Cell(4, 2).Value = resumen?.TotalSql ?? 0;
        hojaResumen.Cell(5, 1).Value = "Conciliados";
        hojaResumen.Cell(5, 2).Value = resumen?.Conciliados ?? 0;
        hojaResumen.Cell(6, 1).Value = "Faltantes SQL";
        hojaResumen.Cell(6, 2).Value = resumen?.FaltantesSql ?? 0;
        hojaResumen.Cell(7, 1).Value = "Faltantes Mongo";
        hojaResumen.Cell(7, 2).Value = resumen?.FaltantesMongo ?? 0;
        hojaResumen.Cell(8, 1).Value = "Diferencias";
        hojaResumen.Cell(8, 2).Value = resumen?.Diferencias ?? 0;
        hojaResumen.Cell(9, 1).Value = "Errores de conexión";
        hojaResumen.Cell(9, 2).Value = resumen?.ErroresConexion ?? 0;
        hojaResumen.Cell(10, 1).Value = "Errores SQL";
        hojaResumen.Cell(10, 2).Value = resumen?.ErroresSql ?? 0;
        hojaResumen.Cell(11, 1).Value = "Configuración no encontrada";
        hojaResumen.Cell(11, 2).Value = resumen?.ConfiguracionNoEncontrada ?? 0;
        hojaResumen.Cell(12, 1).Value = "Locales procesados";
        hojaResumen.Cell(12, 2).Value = resumen?.LocalesProcesados ?? 0;
        hojaResumen.Column(1).AdjustToContents();
        hojaResumen.Column(2).AdjustToContents();

        var hojaDetalle = workbook.Worksheets.Add("Detalle");
        string[] encabezados =
        {
            "Local", "BranchOffice", "ExternalReference", "CodigoApp", "Factura", "Estado",
            "MontoMongo", "MontoSQL", "FechaMongo", "FechaSQL", "Mensaje"
        };

        for (var i = 0; i < encabezados.Length; i++)
        {
            hojaDetalle.Cell(1, i + 1).Value = encabezados[i];
        }

        hojaDetalle.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < resultados.Count; i++)
        {
            var r = resultados[i];
            var fila = i + 2;

            hojaDetalle.Cell(fila, 1).Value = r.Local;
            hojaDetalle.Cell(fila, 2).Value = r.BranchOffice;
            hojaDetalle.Cell(fila, 3).Value = r.ExternalReference;
            hojaDetalle.Cell(fila, 4).Value = r.CodigoApp ?? string.Empty;
            hojaDetalle.Cell(fila, 5).Value = r.CfacId ?? string.Empty;
            hojaDetalle.Cell(fila, 6).Value = r.Estado.ToString();
            hojaDetalle.Cell(fila, 7).Value = r.MongoAmount ?? (decimal?)null;
            hojaDetalle.Cell(fila, 8).Value = r.SqlFpfTotalPagar ?? (decimal?)null;
            hojaDetalle.Cell(fila, 9).Value = r.FechaMongo?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
            hojaDetalle.Cell(fila, 10).Value = r.FechaSql?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
            hojaDetalle.Cell(fila, 11).Value = r.Mensaje;
        }

        if (resultados.Count > 0)
        {
            hojaDetalle.Columns(1, encabezados.Length).AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}