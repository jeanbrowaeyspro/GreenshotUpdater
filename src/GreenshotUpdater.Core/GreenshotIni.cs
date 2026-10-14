using System.Text;
using System.Text.RegularExpressions;

namespace GreenshotUpdater;

/// <summary>
/// Réglage <c>UpdateCheckInterval</c> de la section <c>[Core]</c> de Greenshot.ini
/// (jours entre deux vérifications intégrées ; 0 = désactivée).
/// </summary>
/// <remarks>
/// Seule cette ligne est modifiée : le reste du fichier est réécrit octet pour octet.
/// Un fichier UTF-16 (avec BOM) est traité en UTF-16 ; tout autre fichier est lu en
/// Latin-1, qui fait correspondre un caractère à chaque octet : UTF-8 (avec ou sans
/// BOM) comme ANSI ressortent inchangés, fins de ligne comprises.
/// </remarks>
public static partial class GreenshotIni
{
    public const string Key = "UpdateCheckInterval";
    public const int Disabled = 0;
    /// <summary>Valeur d'origine de Greenshot.</summary>
    public const int Default = 14;

    [GeneratedRegex(@"^[ \t]*\[(?<name>[^\]\r\n]*)\][ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex SectionHeader();

    [GeneratedRegex(@"^(?<prefix>[ \t]*UpdateCheckInterval[ \t]*=[ \t]*)(?<value>[^\r\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex KeyLine();

    /// <summary>Valeur lue dans le texte ; null si la ligne est absente ou illisible.</summary>
    public static int? Read(string text)
    {
        var (start, end) = CoreSection(text);
        if (start < 0)
            return null;
        var match = KeyLine().Match(text[..end], start);
        return match.Success && int.TryParse(match.Groups["value"].Value.Trim(), out var value) ? value : null;
    }

    /// <summary>
    /// Texte avec la nouvelle valeur. La ligne est ajoutée en tête de <c>[Core]</c> si elle
    /// manque, et la section en fin de fichier si elle manque.
    /// </summary>
    public static string With(string text, int value)
    {
        var (start, end) = CoreSection(text);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        if (start < 0)
        {
            var separator = text.Length == 0 || text.EndsWith('\n') ? "" : newline;
            return $"{text}{separator}[Core]{newline}{Key}={value}{newline}";
        }
        var match = KeyLine().Match(text[..end], start);
        if (match.Success)
        {
            var group = match.Groups["value"];
            return string.Concat(text.AsSpan(0, group.Index), value.ToString(), text.AsSpan(group.Index + group.Length));
        }
        // Après la ligne d'en-tête.
        var lineEnd = text.IndexOf('\n', start);
        var insertAt = lineEnd < 0 ? text.Length : lineEnd + 1;
        var prefix = lineEnd < 0 ? newline : "";
        return text.Insert(insertAt, $"{prefix}{Key}={value}{newline}");
    }

    /// <summary>Début (après l'en-tête) et fin de la section [Core] ; (-1, -1) si absente.</summary>
    private static (int Start, int End) CoreSection(string text)
    {
        var headers = SectionHeader().Matches(text);
        for (var i = 0; i < headers.Count; i++)
        {
            if (!headers[i].Groups["name"].Value.Trim().Equals("Core", StringComparison.OrdinalIgnoreCase))
                continue;
            var start = headers[i].Index + headers[i].Length;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            return (start, end);
        }
        return (-1, -1);
    }

    // ------------------------------------------------------------- fichier

    private static Encoding EncodingOf(byte[] bytes) => bytes switch
    {
        [0xFF, 0xFE, ..] => Encoding.Unicode,
        [0xFE, 0xFF, ..] => Encoding.BigEndianUnicode,
        _ => Encoding.Latin1,
    };

    /// <summary>Valeur actuelle dans le fichier ; null si le fichier ou la ligne manque.</summary>
    public static int? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;
        var bytes = File.ReadAllBytes(path);
        return Read(EncodingOf(bytes).GetString(bytes));
    }

    /// <summary>Écrit la valeur dans le fichier en conservant encodage et fins de ligne.</summary>
    public static void WriteFile(string path, int value)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = EncodingOf(bytes);
        // GetString garde le BOM UTF-16 comme caractère U+FEFF : il est réécrit tel quel.
        File.WriteAllBytes(path, encoding.GetBytes(With(encoding.GetString(bytes), value)));
    }
}
