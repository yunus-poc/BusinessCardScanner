using System.Text;
using BusinessCard.Domain.Cards;

namespace BusinessCard.Infrastructure.Llm;

/// <summary>The versioned prompt contract from inference/contract/, embedded at build time.</summary>
internal static class PromptContract
{
    public static string SystemPrompt { get; } = Read("BusinessCard.Llm.system-prompt.txt");

    /// <summary>Raw JSON, written verbatim into each request (no per-request parsing).</summary>
    public static string OutputSchemaJson { get; } = Read("BusinessCard.Llm.classification.schema.json");

    /// <summary>
    /// "2 [CENTER][LARGE] Ahmet Yılmaz": the line id is the reading order, so ids in
    /// the answer map straight back to lines.
    /// </summary>
    public static string FormatLines(IReadOnlyList<CardLine> lines)
    {
        var builder = new StringBuilder(lines.Count * 48);
        foreach (var line in lines)
        {
            builder.Append(line.Order)
                .Append(" [").Append(Tag(line.Region))
                .Append("][").Append(Tag(line.Size))
                .Append("] ").Append(line.Text)
                .Append('\n');
        }
        return builder.ToString();
    }

    private static string Tag<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToUpperInvariant();

    private static string Read(string resourceName)
    {
        using var stream = typeof(PromptContract).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{resourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
