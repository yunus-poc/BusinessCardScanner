using BusinessCard.Application.BusinessCards.Merging;

namespace BusinessCard.UnitTests.Merging;

public sealed class NormalizationTests
{
    [Theory]
    [InlineData("AHMET YILMAZ", true, "Ahmet Yılmaz")]
    [InlineData("ŞEBNEM ÖZTÜRK", true, "Şebnem Öztürk")]
    [InlineData("İLKER IŞIK", true, "İlker Işık")]
    [InlineData("ayşe-nur kaya", true, "Ayşe-Nur Kaya")]
    [InlineData("JOHN SMITH", false, "John Smith")]
    [InlineData("O'NEIL", false, "O'Neil")]
    [InlineData("McDonald", false, "McDonald")]
    [InlineData("Zeynep Kaya", true, "Zeynep Kaya")]
    public void Person_names_are_recased_only_when_printed_in_one_case(string input, bool turkish, string expected)
    {
        Assert.Equal(expected, TextCasing.PersonName(input, turkish));
    }

    [Theory]
    [InlineData("ACME TECHNOLOGY", "ACME Technology")]
    [InlineData("NOVA ENERJİ YATIRIMLARI A.Ş.", "NOVA Enerji Yatırımları A.Ş.")]
    [InlineData("ACME TEKNOLOJİ SAN. VE TİC. LTD. ŞTİ.", "ACME Teknoloji San. ve Tic. Ltd. Şti.")]
    [InlineData("acme gmbh", "Acme GmbH")]
    [InlineData("Acme Teknoloji A.Ş.", "Acme Teknoloji A.Ş.")]
    [InlineData("IBM", "IBM")]
    [InlineData("GLOBAL INNOVATION SYSTEMS", "Global Innovation Systems")]
    public void Company_names_keep_short_brands_and_canonical_legal_forms(string input, string expected)
    {
        Assert.Equal(expected, TextCasing.Company(input));
    }

    [Theory]
    [InlineData("ENTERPRISE SOLUTIONS MANAGER", "Enterprise Solutions Manager")]
    [InlineData("SATIŞ VE PAZARLAMA DİREKTÖRÜ", "Satış ve Pazarlama Direktörü")]
    [InlineData("CEO", "CEO")]
    [InlineData("HEAD OF IT", "Head of IT")]
    [InlineData("Genel Müdür", "Genel Müdür")]
    public void Titles_keep_acronyms_and_lowercase_connectors(string input, string expected)
    {
        Assert.Equal(expected, TextCasing.Title(input));
    }

    [Theory]
    [InlineData("Ahmet Yılmaz", "Ahmet", "Yılmaz")]
    [InlineData("Mehmet Ali Yılmaz", "Mehmet Ali", "Yılmaz")]
    [InlineData("Dr. Mehmet Ali Öz", "Mehmet Ali", "Öz")]
    [InlineData("Prof. Dr. AYŞE KAYA, PhD", "Ayşe", "Kaya")]
    [InlineData("Av. Zeynep Kaya", "Zeynep", "Kaya")]
    [InlineData("Ludwig van Beethoven", "Ludwig", "van Beethoven")]
    [InlineData("Jane Doe MBA", "Jane", "Doe")]
    public void Splits_first_and_last_name(string input, string first, string last)
    {
        var name = NameNormalizer.Normalize(input, turkish: true);

        Assert.NotNull(name);
        Assert.Equal(first, name.First);
        Assert.Equal(last, name.Last);
        Assert.Equal($"{first} {last}", name.Display);
    }

    [Fact]
    public void A_single_word_is_a_first_name_without_surname()
    {
        var name = NameNormalizer.Normalize("Ahmet", turkish: true);

        Assert.Equal(("Ahmet", (string?)null), (name!.First, name.Last));
    }

    [Theory]
    [InlineData(new[] { "Maslak Mah. Büyükdere Cad. No:255", "34485 Sarıyer/İstanbul" }, "34485", "İstanbul", "Maslak Mah. Büyükdere Cad. No:255, Sarıyer")]
    [InlineData(new[] { "Kızılırmak Mah. 1443. Cad. No:25 06510 Çankaya / ANKARA" }, "06510", "Ankara", "Kızılırmak Mah. 1443. Cad. No:25 Çankaya")]
    [InlineData(new[] { "221B Baker Street" }, null, "London", "221B Baker Street")]
    public void Street_excludes_postal_code_and_city(string[] lines, string? postal, string? city, string expected)
    {
        Assert.Equal(expected, AddressNormalizer.Street(lines, postal, city));
    }

    [Theory]
    [InlineData("ISTANBUL", "İstanbul")]
    [InlineData("izmir", "İzmir")]
    [InlineData("LONDON", "London")]
    [InlineData("PARIS", "Paris")]
    [InlineData("ÜSKÜDAR", "Üsküdar")]
    public void Cities_use_canonical_province_names_or_readable_casing(string input, string expected)
    {
        Assert.Equal(expected, AddressNormalizer.City(input));
    }

    [Theory]
    [InlineData("TURKEY", "Türkiye")]
    [InlineData("Türkiye", "Türkiye")]
    [InlineData("TURKIYE", "Türkiye")]
    [InlineData("GERMANY", "Germany")]
    public void Countries_are_canonicalized(string input, string expected)
    {
        Assert.Equal(expected, AddressNormalizer.Country(input));
    }
}
