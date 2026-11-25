using System.Text.RegularExpressions;
using Trackwise.Domain.Entities;

namespace Trackwise.Infrastructure.Documents.Pdf.Shared;

internal static class PdfLabels
{
    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["One"] = "1",
        ["Two"] = "2",
        ["Three"] = "3",
        ["Four"] = "4",
        ["Five"] = "5",
        ["Six"] = "6",
        ["Seven"] = "7",
        ["Eight"] = "8",
        ["Nine"] = "9",
        ["Ten"] = "10",
        ["Twelve"] = "12",
        ["Fourteen"] = "14",
        ["Sixteen"] = "16",
        ["Twenty"] = "20",
    };

    private static readonly Regex NumberWordPattern = new(
        $@"\b({string.Join("|", NumberWords.Keys)})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"Two Tonner" becomes "2 Tonner". Text with no number word is returned unchanged.</summary>
    public static string NumberWordsToDigits(string text) =>
        NumberWordPattern.Replace(text, m => NumberWords[m.Value]);

    public static string TerminalName(Terminal? terminal)
    {
        if (terminal is null)
        {
            return "To be advised";
        }

        var label = $"{terminal.Code} - {terminal.Name}";
        return string.IsNullOrWhiteSpace(terminal.CountryCode) ? label : $"{label} ({terminal.CountryCode})";
    }
}
