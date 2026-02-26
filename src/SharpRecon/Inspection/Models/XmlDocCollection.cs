using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharpRecon.Inspection.Models;

public sealed partial class XmlDocCollection
{
    private readonly Dictionary<string, XmlDocEntry> _entries;

    private XmlDocCollection(Dictionary<string, XmlDocEntry> entries)
    {
        _entries = entries;
    }

    public XmlDocEntry? GetDocumentation(string memberDocId)
    {
        return _entries.GetValueOrDefault(memberDocId);
    }

    public static XmlDocCollection Parse(XDocument document)
    {
        var entries = new Dictionary<string, XmlDocEntry>(StringComparer.Ordinal);

        var members = document.Descendants("member");
        foreach (var member in members)
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var summary = GetCleanedInnerText(member.Element("summary"));
            var returns = GetCleanedInnerText(member.Element("returns"));
            var remarks = GetCleanedInnerText(member.Element("remarks"));

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var param in member.Elements("param"))
            {
                var paramName = param.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(paramName))
                    parameters[paramName] = GetCleanedInnerText(param) ?? string.Empty;
            }

            var exceptions = new List<XmlDocException>();
            foreach (var exception in member.Elements("exception"))
            {
                var cref = exception.Attribute("cref")?.Value;
                if (!string.IsNullOrEmpty(cref))
                {
                    var exceptionType = cref.StartsWith("T:", StringComparison.Ordinal) ? cref[2..] : cref;
                    exceptions.Add(new XmlDocException(exceptionType, GetCleanedInnerText(exception) ?? string.Empty));
                }
            }

            entries[name] = new XmlDocEntry(
                summary,
                parameters,
                returns,
                exceptions,
                remarks);
        }

        return new XmlDocCollection(entries);
    }

    private static string? GetCleanedInnerText(XElement? element)
    {
        if (element is null)
            return null;

        var raw = GetInnerXml(element);
        raw = ProcessSeeElements(raw);
        raw = ProcessParamRefElements(raw);
        raw = StripXmlTags(raw);
        raw = CollapseWhitespace(raw).Trim();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private static string GetInnerXml(XElement element)
    {
        using var reader = element.CreateReader();
        reader.MoveToContent();
        return reader.ReadInnerXml();
    }

    private static string ProcessSeeElements(string text)
    {
        return SeeCrefRegex().Replace(text, match => ExtractShortName(match.Groups[1].Value));
    }

    private static string ProcessParamRefElements(string text)
    {
        return ParamRefRegex().Replace(text, match => match.Groups[1].Value);
    }

    private static string ExtractShortName(string cref)
    {
        var colonIndex = cref.IndexOf(':');
        var name = colonIndex >= 0 ? cref[(colonIndex + 1)..] : cref;
        var parenIndex = name.IndexOf('(');
        var nameBeforeParen = parenIndex >= 0 ? name[..parenIndex] : name;
        var suffix = parenIndex >= 0 ? name[parenIndex..] : string.Empty;
        var lastDot = nameBeforeParen.LastIndexOf('.');
        var shortName = lastDot >= 0 ? nameBeforeParen[(lastDot + 1)..] : nameBeforeParen;
        return shortName + suffix;
    }

    private static string StripXmlTags(string text)
    {
        return XmlTagRegex().Replace(text, string.Empty);
    }

    private static string CollapseWhitespace(string text)
    {
        return WhitespaceRegex().Replace(text, " ");
    }

    [GeneratedRegex("""<see(?:also)?\s+cref\s*=\s*"([^"]*?)"\s*/>""", RegexOptions.Compiled)]
    private static partial Regex SeeCrefRegex();

    [GeneratedRegex("""<(?:paramref|typeparamref)\s+name\s*=\s*"([^"]*?)"\s*/>""", RegexOptions.Compiled)]
    private static partial Regex ParamRefRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex XmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}

public sealed record XmlDocEntry(
    string? Summary,
    IReadOnlyDictionary<string, string> Params,
    string? Returns,
    IReadOnlyList<XmlDocException> Exceptions,
    string? Remarks);

public sealed record XmlDocException(string Type, string Description);
