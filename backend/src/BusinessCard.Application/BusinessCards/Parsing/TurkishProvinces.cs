using System.Text.RegularExpressions;

namespace BusinessCard.Application.BusinessCards.Parsing;

/// <summary>
/// Türkiye's 81 provinces in licence-plate order. The first two digits of a Turkish
/// postal code are the plate code, so "34485" belongs to İstanbul.
/// </summary>
internal static class TurkishProvinces
{
    public const string Country = "Türkiye";

    private static readonly string[] ByPlateCode =
    [
        "Adana", "Adıyaman", "Afyonkarahisar", "Ağrı", "Amasya", "Ankara", "Antalya", "Artvin", "Aydın", "Balıkesir",
        "Bilecik", "Bingöl", "Bitlis", "Bolu", "Burdur", "Bursa", "Çanakkale", "Çankırı", "Çorum", "Denizli",
        "Diyarbakır", "Edirne", "Elazığ", "Erzincan", "Erzurum", "Eskişehir", "Gaziantep", "Giresun", "Gümüşhane", "Hakkari",
        "Hatay", "Isparta", "Mersin", "İstanbul", "İzmir", "Kars", "Kastamonu", "Kayseri", "Kırklareli", "Kırşehir",
        "Kocaeli", "Konya", "Kütahya", "Malatya", "Manisa", "Kahramanmaraş", "Mardin", "Muğla", "Muş", "Nevşehir",
        "Niğde", "Ordu", "Rize", "Sakarya", "Samsun", "Siirt", "Sinop", "Sivas", "Tekirdağ", "Tokat",
        "Trabzon", "Tunceli", "Şanlıurfa", "Uşak", "Van", "Yozgat", "Zonguldak", "Aksaray", "Bayburt", "Karaman",
        "Kırıkkale", "Batman", "Şırnak", "Bartın", "Ardahan", "Iğdır", "Yalova", "Karabük", "Kilis", "Osmaniye",
        "Düzce",
    ];

    /// <summary>Folded name or common short form → canonical name.</summary>
    private static readonly Dictionary<string, string> ByFoldedName = BuildLookup();

    private static readonly Regex FoldedNamePattern = new(
        @"(?<![a-z])(" + string.Join('|', ByFoldedName.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?![a-z])",
        RegexOptions.Compiled);

    public static int Count => ByPlateCode.Length;

    /// <summary>Province for a plate code 1..81, else null.</summary>
    public static string? ForPlateCode(int plateCode) =>
        plateCode is >= 1 and <= 81 ? ByPlateCode[plateCode - 1] : null;

    /// <summary>The last province named in the text ("Maslak, Sarıyer/İstanbul" → "İstanbul").</summary>
    public static string? FindIn(string text)
    {
        var matches = FoldedNamePattern.Matches(TextFold.Fold(text));
        return matches.Count == 0 ? null : ByFoldedName[matches[^1].Value];
    }

    private static Dictionary<string, string> BuildLookup()
    {
        var lookup = ByPlateCode.ToDictionary(TextFold.Fold, name => name);
        // Everyday short forms printed on cards.
        lookup["afyon"] = "Afyonkarahisar";
        lookup["icel"] = "Mersin";
        lookup["antep"] = "Gaziantep";
        lookup["maras"] = "Kahramanmaraş";
        lookup["urfa"] = "Şanlıurfa";
        return lookup;
    }
}
