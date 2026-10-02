using System.Text.RegularExpressions;
using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.Application.BusinessCards.Merging;

/// <summary>
/// Small Turkish/English word lists used as evidence (layout fallback and confidence),
/// never as the only source of a value. All entries are folded (see <see cref="TextFold"/>).
/// </summary>
internal static partial class CardVocabulary
{
    private static readonly HashSet<string> CompanyWords =
    [
        "a.s", "as", "ltd", "sti", "inc", "llc", "gmbh", "corp", "corporation", "co", "company", "plc", "ag", "bv", "sa", "srl",
        "holding", "group", "grup", "grubu", "teknoloji", "technology", "technologies", "yazilim", "software", "solutions",
        "cozumleri", "danismanlik", "consulting", "insaat", "sanayi", "ticaret", "san", "tic", "bank", "bankasi", "sigorta",
        "enerji", "energy", "systems", "sistemleri", "labs", "studio", "ajans", "agency", "partners", "hukuk", "law", "medya",
        "media", "otomotiv", "lojistik", "logistics", "yatirim", "yatirimlari", "investments", "kollektif", "vakfi", "dernegi",
    ];

    private static readonly HashSet<string> TitleWords =
    [
        "manager", "mudur", "muduru", "director", "direktor", "direktoru", "head", "lead", "chief", "ceo", "cto", "cfo",
        "coo", "cio", "cmo", "vp", "president", "baskan", "baskani", "founder", "cofounder", "kurucu", "kurucusu", "partner",
        "ortak", "owner", "consultant", "danisman", "danismani", "specialist", "uzman", "uzmani", "engineer", "muhendis",
        "muhendisi", "developer", "gelistirici", "analyst", "analist", "architect", "mimar", "designer", "tasarimci",
        "coordinator", "koordinator", "koordinatoru", "supervisor", "sorumlu", "sorumlusu", "sef", "sefi", "yonetici",
        "yoneticisi", "executive", "officer", "representative", "temsilci", "temsilcisi", "assistant", "asistan", "avukat",
        "attorney", "lawyer", "advisor", "associate", "intern", "stajyer", "genel", "general", "senior", "kidemli", "junior",
    ];

    private static readonly HashSet<string> DepartmentWords =
    [
        "department", "dept", "departmani", "bolumu", "birimi", "division", "unit", "direktorlugu", "mudurlugu",
    ];

    private static readonly HashSet<string> StreetWords =
    [
        "mah", "mahallesi", "cad", "caddesi", "cd", "sok", "sokak", "sokagi", "sk", "bulvar", "bulvari", "blv", "no", "kat",
        "daire", "plaza", "osb", "pk", "street", "st", "avenue", "ave", "road", "rd", "blvd", "floor", "fl", "suite", "ste",
        "building", "bldg", "is", "merkezi", "residence", "tower", "kule",
    ];

    /// <summary>Webmail domains say nothing about the employer.</summary>
    private static readonly HashSet<string> GenericMailDomains =
    [
        "gmail", "googlemail", "hotmail", "outlook", "live", "msn", "yahoo", "icloud", "me", "yandex", "mail", "aol", "proton", "protonmail", "gmx",
    ];

    public static bool HasCompanyWord(string text) => Words(text).Any(CompanyWords.Contains);

    public static bool HasTitleWord(string text) => Words(text).Any(TitleWords.Contains);

    public static bool HasDepartmentWord(string text) => Words(text).Any(DepartmentWords.Contains);

    public static bool IsGenericMailDomain(string label) => GenericMailDomains.Contains(label);

    public static bool LooksLikeAddress(string text) =>
        Words(text).Any(StreetWords.Contains) || FiveDigits().IsMatch(text) || SlashPlace().IsMatch(text);

    /// <summary>
    /// 2–4 words of letters (plus . ' - ), no digits, no company/title vocabulary:
    /// "Ahmet Yılmaz", "Dr. Mehmet Ali Öz", "Anne-Marie O'Neil".
    /// </summary>
    public static bool LooksLikePersonName(string text)
    {
        if (text.Any(char.IsDigit) || text.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameWords = words.Count(w => NameWord().IsMatch(w) && w.Count(char.IsLetter) >= 2);
        return nameWords is >= 2 and <= 4
            && words.Length <= 5
            && !HasCompanyWord(text)
            && !HasTitleWord(text)
            && !LooksLikeAddress(text);
    }

    /// <summary>Folded words: "Satış Direktörü, A.Ş." → ["satis", "direktoru", "a.s"].</summary>
    public static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(TextFold.Fold(text)).Select(m => m.Value.TrimEnd('.'));

    [GeneratedRegex(@"[a-z0-9]+(?:\.[a-z0-9]+)*\.?")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"^\p{L}[\p{L}'’.\-]*$")]
    private static partial Regex NameWord();

    [GeneratedRegex(@"(?<!\d)\d{5}(?!\d)")]
    private static partial Regex FiveDigits();

    /// <summary>"Sarıyer/İstanbul", "Kadıköy / Istanbul".</summary>
    [GeneratedRegex(@"\p{L}+\s*/\s*\p{L}+")]
    private static partial Regex SlashPlace();
}
