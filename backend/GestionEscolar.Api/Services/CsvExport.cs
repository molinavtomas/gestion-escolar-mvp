using System.Text;

namespace GestionEscolar.Api.Services;

/// <summary>
/// Genera archivos CSV que Excel abre directamente (configuración regional de Argentina):
///  - separador ";" (Excel en español usa la coma como separador decimal);
///  - codificación UTF-8 con BOM, para que las tildes y la ñ se vean bien.
/// Es una alternativa simple a generar .xlsx sin agregar librerías al proyecto.
/// </summary>
public static class CsvExport
{
    public static byte[] Build(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', header.Select(Escape)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(';', row.Select(v => Escape(Format(v)))));
        }
        // GetPreamble() = BOM de UTF-8; sin él Excel interpreta mal los acentos.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime d => d.ToString("dd/MM/yyyy HH:mm"),
        DateOnly d => d.ToString("dd/MM/yyyy"),
        bool b => b ? "Sí" : "No",
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>Encierra entre comillas los valores con ; comillas o saltos de línea.</summary>
    private static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
