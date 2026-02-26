using SharpRecon.Inspection;
using SharpRecon.Inspection.Models;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Inspection;

public sealed class XmlDocParserTests : IDisposable
{
    private readonly string _tempDir;
    private readonly XmlDocParser _parser = new();

    public XmlDocParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SharpRecon.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteXml(string xml, string fileName = "test.xml")
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void ParseTypeDoc_ReturnsSummary()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:MyNamespace.MyClass">
                  <summary>This is MyClass.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/MyAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("T:MyNamespace.MyClass");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("This is MyClass.");
    }

    [Fact]
    public void ParseMethodWithParamsReturnsExceptions()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:MyNamespace.MyClass.DoStuff(System.String,System.Int32)">
                  <summary>Does stuff with input.</summary>
                  <param name="input">The input string.</param>
                  <param name="count">How many times.</param>
                  <returns>A result string.</returns>
                  <exception cref="T:System.ArgumentNullException">When input is null.</exception>
                  <exception cref="T:System.ArgumentOutOfRangeException">When count is negative.</exception>
                  <remarks>Use with caution.</remarks>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/MethodAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("M:MyNamespace.MyClass.DoStuff(System.String,System.Int32)");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Does stuff with input.");
        entry.Params.Count.ShouldBe(2);
        entry.Params["input"].ShouldBe("The input string.");
        entry.Params["count"].ShouldBe("How many times.");
        entry.Returns.ShouldBe("A result string.");
        entry.Exceptions.Count.ShouldBe(2);
        entry.Exceptions[0].Type.ShouldBe("System.ArgumentNullException");
        entry.Exceptions[0].Description.ShouldBe("When input is null.");
        entry.Exceptions[1].Type.ShouldBe("System.ArgumentOutOfRangeException");
        entry.Exceptions[1].Description.ShouldBe("When count is negative.");
        entry.Remarks.ShouldBe("Use with caution.");
    }

    [Fact]
    public void ParsePropertyDoc()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="P:MyNamespace.MyClass.Name">
                  <summary>Gets or sets the name.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/PropAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("P:MyNamespace.MyClass.Name");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Gets or sets the name.");
    }

    [Fact]
    public void ParseGenericMethodDocId()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:Foo.Bar`1(``0)">
                  <summary>A generic method.</summary>
                  <param name="value">The value of generic type.</param>
                  <returns>The converted result.</returns>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/GenericAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("M:Foo.Bar`1(``0)");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("A generic method.");
        entry.Params["value"].ShouldBe("The value of generic type.");
        entry.Returns.ShouldBe("The converted result.");
    }

    [Fact]
    public void ParseOverloadedMethods_DifferentDocIds()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:MyNamespace.Converter.Convert(System.String)">
                  <summary>Converts a string.</summary>
                </member>
                <member name="M:MyNamespace.Converter.Convert(System.Int32)">
                  <summary>Converts an integer.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/OverloadAssembly");

        collection.ShouldNotBeNull();

        var stringOverload = collection.GetDocumentation("M:MyNamespace.Converter.Convert(System.String)");
        stringOverload.ShouldNotBeNull();
        stringOverload.Summary.ShouldBe("Converts a string.");

        var intOverload = collection.GetDocumentation("M:MyNamespace.Converter.Convert(System.Int32)");
        intOverload.ShouldNotBeNull();
        intOverload.Summary.ShouldBe("Converts an integer.");
    }

    [Fact]
    public void CachingReturnsSameInstance()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:Cached.Type">
                  <summary>Cached type.</summary>
                </member>
              </members>
            </doc>
            """);

        var cacheKey = "pkg/1.0/net10.0/CacheAssembly";

        var first = _parser.LoadForAssembly(path, cacheKey);
        var second = _parser.LoadForAssembly(path, cacheKey);

        first.ShouldNotBeNull();
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void MissingXmlFileReturnsNull()
    {
        var nonExistentPath = Path.Combine(_tempDir, "does-not-exist.xml");

        var result = _parser.LoadForAssembly(nonExistentPath, "pkg/1.0/net10.0/Missing");

        result.ShouldBeNull();
    }

    [Fact]
    public void SeeCrefTagIsStrippedToMemberName()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:MyNamespace.MyClass.Process">
                  <summary>Processes using <see cref="T:MyNamespace.Processor"/>.</summary>
                  <returns>A <see cref="T:MyNamespace.Result"/> object.</returns>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/SeeAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("M:MyNamespace.MyClass.Process");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Processes using Processor.");
        entry.Returns.ShouldBe("A Result object.");
    }

    [Fact]
    public void SeeCrefWithMethodReference_StrippedToMethodName()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:MyNamespace.MyClass.Validate">
                  <summary>Call <see cref="M:MyNamespace.Helper.Check(System.String)"/> first.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/SeeMemberAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("M:MyNamespace.MyClass.Validate");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Call Check(System.String) first.");
    }

    [Fact]
    public void MissingDocIdReturnsNull()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:MyNamespace.Exists">
                  <summary>I exist.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/LookupAssembly");

        collection.ShouldNotBeNull();
        collection.GetDocumentation("T:MyNamespace.DoesNotExist").ShouldBeNull();
    }

    [Fact]
    public void MultilineWhitespaceIsCollapsed()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:MyNamespace.Formatted">
                  <summary>
                    This is a multiline
                    summary that should be
                    collapsed into one line.
                  </summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/FormattedAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("T:MyNamespace.Formatted");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("This is a multiline summary that should be collapsed into one line.");
    }

    [Fact]
    public void ParamRefIsReplacedWithParameterName()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:MyNamespace.MyClass.Process(System.String)">
                  <summary>Processes the <paramref name="input"/> value.</summary>
                  <param name="input">The input to process.</param>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/ParamRefAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("M:MyNamespace.MyClass.Process(System.String)");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Processes the input value.");
    }

    [Fact]
    public void TypeParamRefIsReplacedWithParameterName()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:MyNamespace.MyClass.Convert``1(``0)">
                  <summary>Converts a value of type <typeparamref name="T"/>.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/TypeParamRefAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("M:MyNamespace.MyClass.Convert``1(``0)");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Converts a value of type T.");
    }

    [Fact]
    public void SeeAlsoCrefIsReplacedWithShortName()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:MyNamespace.MyClass">
                  <summary>A class. <seealso cref="T:MyNamespace.OtherClass"/></summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/SeeAlsoAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("T:MyNamespace.MyClass");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("A class. OtherClass");
    }

    [Fact]
    public void FieldDocIsParsed()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="F:MyNamespace.MyClass.MaxRetries">
                  <summary>Maximum number of retries.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/FieldAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("F:MyNamespace.MyClass.MaxRetries");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Maximum number of retries.");
    }

    [Fact]
    public void EventDocIsParsed()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="E:MyNamespace.MyClass.Changed">
                  <summary>Raised when the value changes.</summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/EventAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("E:MyNamespace.MyClass.Changed");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe("Raised when the value changes.");
    }

    [Fact]
    public void EmptySummaryReturnsNull()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:MyNamespace.Empty">
                  <summary>   </summary>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/EmptyAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("T:MyNamespace.Empty");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBeNull();
    }

    [Fact]
    public void ExceptionCrefWithoutTPrefixIsPreservedAsIs()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="M:MyNamespace.MyClass.Run">
                  <exception cref="System.InvalidOperationException">Always.</exception>
                </member>
              </members>
            </doc>
            """);

        var collection = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/ExCrefAssembly");

        collection.ShouldNotBeNull();
        var entry = collection.GetDocumentation("M:MyNamespace.MyClass.Run");
        entry.ShouldNotBeNull();
        entry.Exceptions.Count.ShouldBe(1);
        entry.Exceptions[0].Type.ShouldBe("System.InvalidOperationException");
    }

    [Fact]
    public void DifferentCacheKeysLoadSeparateInstances()
    {
        var path = WriteXml("""
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:X.Y">
                  <summary>Type Y.</summary>
                </member>
              </members>
            </doc>
            """);

        var first = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/A");
        var second = _parser.LoadForAssembly(path, "pkg/1.0/net10.0/B");

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        ReferenceEquals(first, second).ShouldBeFalse();
    }
}
