using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpRecon.Inspection.Models;

namespace SharpRecon.Inspection;

public sealed class XmlDocParser
{
    private readonly ConcurrentDictionary<string, XmlDocCollection?> _cache = new(StringComparer.Ordinal);

    public XmlDocCollection? LoadForAssembly(string xmlFilePath, string cacheKey)
    {
        return _cache.GetOrAdd(cacheKey, _ =>
        {
            if (!File.Exists(xmlFilePath))
                return null;

            var document = XDocument.Load(xmlFilePath);
            return XmlDocCollection.Parse(document);
        });
    }

    public static IServiceCollection AddXmlDocParser(IServiceCollection services)
    {
        services.AddSingleton<XmlDocParser>();
        return services;
    }
}
