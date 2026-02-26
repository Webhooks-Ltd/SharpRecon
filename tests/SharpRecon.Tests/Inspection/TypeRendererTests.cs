using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpRecon.Inspection;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Inspection;

public sealed class TypeRendererTests : IDisposable
{
    private readonly MetadataLoadContext _mlc;
    private readonly Assembly _testAssembly;

    private const string TestSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace TestTypes
        {
            public class SimpleClass { }

            public sealed class SealedClass { }

            public abstract class AbstractClass
            {
                public abstract void DoSomething();
            }

            public static class StaticClass
            {
                public static void Utility() { }
            }

            public class GenericClass<T> { }

            public class ConstrainedGeneric<T, U>
                where T : class, new()
                where U : struct
            {
            }

            public class BaseClassConstraint<T> where T : Exception, IDisposable
            {
            }

            public interface ISimpleInterface
            {
                void Execute();
            }

            public interface IGenericInterface<T>
            {
                T GetValue();
            }

            public interface IConstrainedInterface<T> where T : IComparable<T>
            {
            }

            public interface ICovariantInterface<out T>
            {
                T GetValue();
            }

            public interface IContravariantInterface<in T>
            {
                void Process(T value);
            }

            public enum SimpleEnum
            {
                None,
                First,
                Second
            }

            public enum ByteEnum : byte
            {
                Zero,
                One
            }

            [Flags]
            public enum FlagsEnum
            {
                None = 0,
                Read = 1,
                Write = 2
            }

            public delegate void SimpleDelegate();

            public delegate TResult GenericDelegate<in T, out TResult>(T input);

            public delegate void ConstrainedDelegate<T>(T value) where T : class;

            public record SimpleRecord(string Name, int Age);

            public record struct MutableRecordStruct(double X, double Y);

            public readonly record struct ReadOnlyRecordStruct(double X, double Y);

            public record SealedRecord(string Value);

            public class ClassWithBaseType : Exception
            {
            }

            public class ClassWithInterfaces : IDisposable, IComparable<ClassWithInterfaces>
            {
                public void Dispose() { }
                public int CompareTo(ClassWithInterfaces? other) => 0;
            }

            public class ClassWithBaseAndInterfaces : Exception, IDisposable
            {
                public void Dispose() { }
            }

            public readonly struct ReadOnlyStruct
            {
                public readonly int Value;
                public ReadOnlyStruct(int value) { Value = value; }
            }

            public ref struct RefStruct
            {
                public int Value;
            }

            public class Outer
            {
                public class Inner { }
                public class GenericInner<T> { }
            }

            public class MethodTestClass
            {
                public void SimpleMethod() { }
                public static void StaticMethod() { }
                public virtual void VirtualMethod() { }
                public int ReturnsInt() => 0;
                public string ReturnsString() => "";
                public List<int> ReturnsList() => new();

                public void RefParam(ref int x) { }
                public void OutParam(out int x) { x = 0; }
                public void InParam(in int x) { }
                public void ParamsParam(params int[] values) { }

                public void DefaultString(string value = "hello") { }
                public void DefaultInt(int count = 42) { }
                public void DefaultBool(bool flag = true) { }
                public void DefaultNull(string? value = null) { }
                public void DefaultEnum(StringComparison comparison = StringComparison.Ordinal) { }
                public void DefaultChar(char ch = 'x') { }
                public void DefaultLong(long value = 100L) { }
                public void DefaultFloat(float value = 1.5f) { }
                public void DefaultDouble(double value = 2.5d) { }

                public T GenericMethod<T>(T value) where T : class => value;
                public T MultiConstraint<T>(T value) where T : IComparable<T>, new() => value;

                public void MultipleParams(string name, int age, bool active = false) { }
            }

            public class ConstructorTestClass
            {
                public ConstructorTestClass() { }
                public ConstructorTestClass(string name) { }
                public ConstructorTestClass(string name, int age) { }
            }

            public class PropertyTestClass
            {
                public string ReadWrite { get; set; } = "";
                public string ReadOnly { get; }
                public string InitOnly { get; init; } = "";
                public static int StaticProp { get; set; }
                public int this[int index] => index;
            }

            public abstract class AbstractPropertyClass
            {
                public abstract string AbstractProp { get; set; }
                public virtual string VirtualProp { get; set; } = "";
            }

            public class FieldTestClass
            {
                public int InstanceField;
                public static int StaticField;
                public readonly int ReadOnlyField;
                public const int ConstField = 42;
                public const string StringConst = "hello";
                public const bool BoolConst = true;
            }

            public class EventTestClass
            {
                public event EventHandler? SimpleEvent;
                public static event EventHandler? StaticEvent;
            }

            public class OverrideTestClass : AbstractClass
            {
                public override void DoSomething() { }
            }

            public sealed class SealedOverrideClass : AbstractClass
            {
                public sealed override void DoSomething() { }
            }

        #nullable enable
            public class NullableTestClass
            {
                public string NonNullString { get; set; } = "";
                public string? NullableString { get; set; }
                public int NonNullInt { get; set; }
                public int? NullableInt { get; set; }
                public List<string?> ListOfNullableStrings { get; set; } = new();
                public List<string>? NullableListOfStrings { get; set; }
                public Dictionary<string, string?> DictWithNullableValues { get; set; } = new();

                public string? NullableMethod(string? input, int? count) => input;
                public void NonNullParams(string required, int count) { }
            }
        #nullable restore

            public class TypeAliasTestClass
            {
                public bool BoolProp { get; set; }
                public byte ByteProp { get; set; }
                public sbyte SByteProp { get; set; }
                public char CharProp { get; set; }
                public decimal DecimalProp { get; set; }
                public double DoubleProp { get; set; }
                public float FloatProp { get; set; }
                public short ShortProp { get; set; }
                public int IntProp { get; set; }
                public long LongProp { get; set; }
                public ushort UShortProp { get; set; }
                public uint UIntProp { get; set; }
                public ulong ULongProp { get; set; }
                public object ObjectProp { get; set; } = new();
                public string StringProp { get; set; } = "";
                public nint NIntProp { get; set; }
                public nuint NUIntProp { get; set; }
            }

            public class ArrayTestClass
            {
                public int[] IntArray { get; set; } = Array.Empty<int>();
                public string[] StringArray { get; set; } = Array.Empty<string>();
                public int[,] MultiDimArray { get; set; } = new int[0, 0];
            }

            public class ProtectedMemberClass
            {
                public int PublicProp { get; set; }
                public int PublicGetProtectedSet { get; protected set; }
            }
        }
        """;

    public TypeRendererTests()
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var runtimeAssemblies = Directory.GetFiles(runtimeDir, "*.dll");
        var resolver = new PathAssemblyResolver(runtimeAssemblies);
        _mlc = new MetadataLoadContext(resolver, "System.Runtime");

        var testDllBytes = CompileTestAssembly();
        _testAssembly = _mlc.LoadFromByteArray(testDllBytes);
    }

    public void Dispose()
    {
        _mlc.Dispose();
    }

    private static byte[] CompileTestAssembly()
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var references = new[]
        {
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.InteropServices.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Private.CoreLib.dll")),
        };

        var syntaxTree = CSharpSyntaxTree.ParseText(TestSource);
        var compilation = CSharpCompilation.Create(
            "TestTypes",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Test assembly compilation failed:\n{errors}");
        }

        return ms.ToArray();
    }

    private Type GetTestType(string name) => _testAssembly.GetType($"TestTypes.{name}")!;

    // === Type Declaration Tests ===

    [Fact]
    public void RenderTypeDeclaration_SimpleClass()
    {
        var type = GetTestType("SimpleClass");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public class SimpleClass");
    }

    [Fact]
    public void RenderTypeDeclaration_SealedClass()
    {
        var type = GetTestType("SealedClass");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public sealed class SealedClass");
    }

    [Fact]
    public void RenderTypeDeclaration_AbstractClass()
    {
        var type = GetTestType("AbstractClass");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public abstract class AbstractClass");
    }

    [Fact]
    public void RenderTypeDeclaration_StaticClass()
    {
        var type = GetTestType("StaticClass");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public static class StaticClass");
    }

    [Fact]
    public void RenderTypeDeclaration_GenericClass()
    {
        var type = GetTestType("GenericClass`1");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public class GenericClass<T>");
    }

    [Fact]
    public void RenderTypeDeclaration_GenericWithConstraints()
    {
        var type = GetTestType("ConstrainedGeneric`2");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public class ConstrainedGeneric<T, U> where T : class, new() where U : struct");
    }

    [Fact]
    public void RenderTypeDeclaration_BaseClassConstraint()
    {
        var type = GetTestType("BaseClassConstraint`1");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public class BaseClassConstraint<T> where T : Exception, IDisposable");
    }

    [Fact]
    public void RenderTypeDeclaration_SimpleInterface()
    {
        var type = GetTestType("ISimpleInterface");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public interface ISimpleInterface");
    }

    [Fact]
    public void RenderTypeDeclaration_GenericInterface()
    {
        var type = GetTestType("IGenericInterface`1");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public interface IGenericInterface<T>");
    }

    [Fact]
    public void RenderTypeDeclaration_ConstrainedInterface()
    {
        var type = GetTestType("IConstrainedInterface`1");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public interface IConstrainedInterface<T> where T : IComparable<T>");
    }

    [Fact]
    public void RenderTypeDeclaration_CovariantInterface()
    {
        var type = GetTestType("ICovariantInterface`1");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public interface ICovariantInterface<out T>");
    }

    [Fact]
    public void RenderTypeDeclaration_ContravariantInterface()
    {
        var type = GetTestType("IContravariantInterface`1");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public interface IContravariantInterface<in T>");
    }

    [Fact]
    public void RenderTypeDeclaration_SimpleEnum()
    {
        var type = GetTestType("SimpleEnum");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public enum SimpleEnum");
    }

    [Fact]
    public void RenderTypeDeclaration_ByteEnum()
    {
        var type = GetTestType("ByteEnum");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public enum ByteEnum : byte");
    }

    [Fact]
    public void RenderTypeDeclaration_SimpleDelegate()
    {
        var type = GetTestType("SimpleDelegate");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public delegate void SimpleDelegate()");
    }

    [Fact]
    public void RenderTypeDeclaration_GenericDelegate()
    {
        var type = GetTestType("GenericDelegate`2");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public delegate TResult GenericDelegate<in T, out TResult>(T input)");
    }

    [Fact]
    public void RenderTypeDeclaration_ConstrainedDelegate()
    {
        var type = GetTestType("ConstrainedDelegate`1");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public delegate void ConstrainedDelegate<T>(T value) where T : class");
    }

    [Fact]
    public void RenderTypeDeclaration_Record()
    {
        var type = GetTestType("SimpleRecord");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public record SimpleRecord : IEquatable<SimpleRecord>");
    }

    [Fact]
    public void RenderTypeDeclaration_MutableRecordStruct()
    {
        var type = GetTestType("MutableRecordStruct");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public record struct MutableRecordStruct : IEquatable<MutableRecordStruct>");
    }

    [Fact]
    public void RenderTypeDeclaration_ReadOnlyRecordStruct()
    {
        var type = GetTestType("ReadOnlyRecordStruct");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public readonly record struct ReadOnlyRecordStruct : IEquatable<ReadOnlyRecordStruct>");
    }

    [Fact]
    public void RenderTypeDeclaration_ClassWithBaseType()
    {
        var type = GetTestType("ClassWithBaseType");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public class ClassWithBaseType : Exception");
    }

    [Fact]
    public void RenderTypeDeclaration_ClassWithInterfaces()
    {
        var type = GetTestType("ClassWithInterfaces");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldContain("IDisposable");
        result.ShouldContain("IComparable<ClassWithInterfaces>");
        result.ShouldStartWith("public class ClassWithInterfaces :");
    }

    [Fact]
    public void RenderTypeDeclaration_ClassWithBaseAndInterfaces()
    {
        var type = GetTestType("ClassWithBaseAndInterfaces");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldStartWith("public class ClassWithBaseAndInterfaces : Exception");
        result.ShouldContain("IDisposable");
    }

    [Fact]
    public void RenderTypeDeclaration_ReadOnlyStruct()
    {
        var type = GetTestType("ReadOnlyStruct");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public readonly struct ReadOnlyStruct");
    }

    [Fact]
    public void RenderTypeDeclaration_RefStruct()
    {
        var type = GetTestType("RefStruct");
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public ref struct RefStruct");
    }

    [Fact]
    public void RenderTypeDeclaration_NestedType()
    {
        var type = _testAssembly.GetType("TestTypes.Outer+Inner")!;
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public class Outer.Inner");
    }

    [Fact]
    public void RenderTypeDeclaration_NestedGenericType()
    {
        var type = _testAssembly.GetType("TestTypes.Outer+GenericInner`1")!;
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public class Outer.GenericInner<T>");
    }

    // === Method Signature Tests ===

    [Fact]
    public void RenderMethodSignature_SimpleMethod()
    {
        var method = GetTestType("MethodTestClass").GetMethod("SimpleMethod")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void SimpleMethod()");
    }

    [Fact]
    public void RenderMethodSignature_StaticMethod()
    {
        var method = GetTestType("MethodTestClass").GetMethod("StaticMethod")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public static void StaticMethod()");
    }

    [Fact]
    public void RenderMethodSignature_VirtualMethod()
    {
        var method = GetTestType("MethodTestClass").GetMethod("VirtualMethod")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public virtual void VirtualMethod()");
    }

    [Fact]
    public void RenderMethodSignature_ReturnsInt()
    {
        var method = GetTestType("MethodTestClass").GetMethod("ReturnsInt")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public int ReturnsInt()");
    }

    [Fact]
    public void RenderMethodSignature_ReturnsString()
    {
        var method = GetTestType("MethodTestClass").GetMethod("ReturnsString")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public string ReturnsString()");
    }

    [Fact]
    public void RenderMethodSignature_ReturnsList()
    {
        var method = GetTestType("MethodTestClass").GetMethod("ReturnsList")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public List<int> ReturnsList()");
    }

    [Fact]
    public void RenderMethodSignature_RefParam()
    {
        var method = GetTestType("MethodTestClass").GetMethod("RefParam")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void RefParam(ref int x)");
    }

    [Fact]
    public void RenderMethodSignature_OutParam()
    {
        var method = GetTestType("MethodTestClass").GetMethod("OutParam")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void OutParam(out int x)");
    }

    [Fact]
    public void RenderMethodSignature_InParam()
    {
        var method = GetTestType("MethodTestClass").GetMethod("InParam")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void InParam(in int x)");
    }

    [Fact]
    public void RenderMethodSignature_ParamsParam()
    {
        var method = GetTestType("MethodTestClass").GetMethod("ParamsParam")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void ParamsParam(params int[] values)");
    }

    [Fact]
    public void RenderMethodSignature_DefaultString()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultString")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultString(string value = \"hello\")");
    }

    [Fact]
    public void RenderMethodSignature_DefaultInt()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultInt")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultInt(int count = 42)");
    }

    [Fact]
    public void RenderMethodSignature_DefaultBool()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultBool")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultBool(bool flag = true)");
    }

    [Fact]
    public void RenderMethodSignature_DefaultNull()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultNull")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultNull(string? value = null)");
    }

    [Fact]
    public void RenderMethodSignature_DefaultEnum()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultEnum")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultEnum(StringComparison comparison = StringComparison.Ordinal)");
    }

    [Fact]
    public void RenderMethodSignature_DefaultChar()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultChar")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultChar(char ch = 'x')");
    }

    [Fact]
    public void RenderMethodSignature_DefaultLong()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultLong")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultLong(long value = 100L)");
    }

    [Fact]
    public void RenderMethodSignature_DefaultFloat()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultFloat")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultFloat(float value = 1.5f)");
    }

    [Fact]
    public void RenderMethodSignature_DefaultDouble()
    {
        var method = GetTestType("MethodTestClass").GetMethod("DefaultDouble")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void DefaultDouble(double value = 2.5d)");
    }

    [Fact]
    public void RenderMethodSignature_GenericMethod()
    {
        var method = GetTestType("MethodTestClass").GetMethod("GenericMethod")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public T GenericMethod<T>(T value) where T : class");
    }

    [Fact]
    public void RenderMethodSignature_MultiConstraint()
    {
        var method = GetTestType("MethodTestClass").GetMethod("MultiConstraint")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public T MultiConstraint<T>(T value) where T : IComparable<T>, new()");
    }

    [Fact]
    public void RenderMethodSignature_MultipleParams()
    {
        var method = GetTestType("MethodTestClass").GetMethod("MultipleParams")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void MultipleParams(string name, int age, bool active = false)");
    }

    [Fact]
    public void RenderMethodSignature_Override()
    {
        var method = GetTestType("OverrideTestClass").GetMethod("DoSomething")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public override void DoSomething()");
    }

    [Fact]
    public void RenderMethodSignature_SealedOverride()
    {
        var method = GetTestType("SealedOverrideClass").GetMethod("DoSomething")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public sealed override void DoSomething()");
    }

    // === Constructor Tests ===

    [Fact]
    public void RenderConstructorSignature_Parameterless()
    {
        var ctor = GetTestType("ConstructorTestClass")
            .GetConstructors()
            .First(c => c.GetParameters().Length == 0);
        var result = TypeRenderer.RenderConstructorSignature(ctor);
        result.ShouldBe("public ConstructorTestClass()");
    }

    [Fact]
    public void RenderConstructorSignature_SingleParam()
    {
        var ctor = GetTestType("ConstructorTestClass")
            .GetConstructors()
            .First(c => c.GetParameters().Length == 1);
        var result = TypeRenderer.RenderConstructorSignature(ctor);
        result.ShouldBe("public ConstructorTestClass(string name)");
    }

    [Fact]
    public void RenderConstructorSignature_MultipleParams()
    {
        var ctor = GetTestType("ConstructorTestClass")
            .GetConstructors()
            .First(c => c.GetParameters().Length == 2);
        var result = TypeRenderer.RenderConstructorSignature(ctor);
        result.ShouldBe("public ConstructorTestClass(string name, int age)");
    }

    // === Property Tests ===

    [Fact]
    public void RenderPropertySignature_ReadWrite()
    {
        var prop = GetTestType("PropertyTestClass").GetProperty("ReadWrite")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public string ReadWrite { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_ReadOnly()
    {
        var prop = GetTestType("PropertyTestClass").GetProperty("ReadOnly")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public string ReadOnly { get; }");
    }

    [Fact]
    public void RenderPropertySignature_InitOnly()
    {
        var prop = GetTestType("PropertyTestClass").GetProperty("InitOnly")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public string InitOnly { get; init; }");
    }

    [Fact]
    public void RenderPropertySignature_Static()
    {
        var prop = GetTestType("PropertyTestClass").GetProperty("StaticProp")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public static int StaticProp { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_Indexer()
    {
        var prop = GetTestType("PropertyTestClass").GetProperty("Item")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public int this[int index] { get; }");
    }

    [Fact]
    public void RenderPropertySignature_Abstract()
    {
        var prop = GetTestType("AbstractPropertyClass").GetProperty("AbstractProp")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public abstract string AbstractProp { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_Virtual()
    {
        var prop = GetTestType("AbstractPropertyClass").GetProperty("VirtualProp")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public virtual string VirtualProp { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_ProtectedSet()
    {
        var prop = GetTestType("ProtectedMemberClass").GetProperty("PublicGetProtectedSet")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public int PublicGetProtectedSet { get; protected set; }");
    }

    // === Field Tests ===

    [Fact]
    public void RenderFieldSignature_InstanceField()
    {
        var field = GetTestType("FieldTestClass").GetField("InstanceField")!;
        var result = TypeRenderer.RenderFieldSignature(field);
        result.ShouldBe("public int InstanceField");
    }

    [Fact]
    public void RenderFieldSignature_StaticField()
    {
        var field = GetTestType("FieldTestClass").GetField("StaticField")!;
        var result = TypeRenderer.RenderFieldSignature(field);
        result.ShouldBe("public static int StaticField");
    }

    [Fact]
    public void RenderFieldSignature_ReadOnlyField()
    {
        var field = GetTestType("FieldTestClass").GetField("ReadOnlyField")!;
        var result = TypeRenderer.RenderFieldSignature(field);
        result.ShouldBe("public readonly int ReadOnlyField");
    }

    [Fact]
    public void RenderFieldSignature_ConstInt()
    {
        var field = GetTestType("FieldTestClass").GetField("ConstField")!;
        var result = TypeRenderer.RenderFieldSignature(field);
        result.ShouldBe("public const int ConstField = 42");
    }

    [Fact]
    public void RenderFieldSignature_ConstString()
    {
        var field = GetTestType("FieldTestClass").GetField("StringConst")!;
        var result = TypeRenderer.RenderFieldSignature(field);
        result.ShouldBe("public const string StringConst = \"hello\"");
    }

    [Fact]
    public void RenderFieldSignature_ConstBool()
    {
        var field = GetTestType("FieldTestClass").GetField("BoolConst")!;
        var result = TypeRenderer.RenderFieldSignature(field);
        result.ShouldBe("public const bool BoolConst = true");
    }

    // === Event Tests ===

    [Fact]
    public void RenderEventSignature_Simple()
    {
        var evt = GetTestType("EventTestClass").GetEvent("SimpleEvent")!;
        var result = TypeRenderer.RenderEventSignature(evt);
        result.ShouldBe("public event EventHandler? SimpleEvent");
    }

    [Fact]
    public void RenderEventSignature_Static()
    {
        var evt = GetTestType("EventTestClass").GetEvent("StaticEvent")!;
        var result = TypeRenderer.RenderEventSignature(evt);
        result.ShouldBe("public static event EventHandler? StaticEvent");
    }

    // === Nullable Annotation Tests ===

    [Fact]
    public void RenderPropertySignature_NullableString()
    {
        var prop = GetTestType("NullableTestClass").GetProperty("NullableString")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public string? NullableString { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_NonNullString()
    {
        var prop = GetTestType("NullableTestClass").GetProperty("NonNullString")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public string NonNullString { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_NullableInt()
    {
        var prop = GetTestType("NullableTestClass").GetProperty("NullableInt")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public int? NullableInt { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_ListOfNullableStrings()
    {
        var prop = GetTestType("NullableTestClass").GetProperty("ListOfNullableStrings")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public List<string?> ListOfNullableStrings { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_NullableListOfStrings()
    {
        var prop = GetTestType("NullableTestClass").GetProperty("NullableListOfStrings")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public List<string>? NullableListOfStrings { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_DictWithNullableValues()
    {
        var prop = GetTestType("NullableTestClass").GetProperty("DictWithNullableValues")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public Dictionary<string, string?> DictWithNullableValues { get; set; }");
    }

    [Fact]
    public void RenderMethodSignature_NullableMethod()
    {
        var method = GetTestType("NullableTestClass").GetMethod("NullableMethod")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public string? NullableMethod(string? input, int? count)");
    }

    [Fact]
    public void RenderMethodSignature_NonNullParams()
    {
        var method = GetTestType("NullableTestClass").GetMethod("NonNullParams")!;
        var result = TypeRenderer.RenderMethodSignature(method);
        result.ShouldBe("public void NonNullParams(string required, int count)");
    }

    // === Type Alias Tests ===

    [Fact]
    public void RenderTypeName_BuiltInAliases()
    {
        var type = GetTestType("TypeAliasTestClass");
        var props = type.GetProperties();

        GetRenderedPropertyType(props, "BoolProp").ShouldBe("bool");
        GetRenderedPropertyType(props, "ByteProp").ShouldBe("byte");
        GetRenderedPropertyType(props, "SByteProp").ShouldBe("sbyte");
        GetRenderedPropertyType(props, "CharProp").ShouldBe("char");
        GetRenderedPropertyType(props, "DecimalProp").ShouldBe("decimal");
        GetRenderedPropertyType(props, "DoubleProp").ShouldBe("double");
        GetRenderedPropertyType(props, "FloatProp").ShouldBe("float");
        GetRenderedPropertyType(props, "ShortProp").ShouldBe("short");
        GetRenderedPropertyType(props, "IntProp").ShouldBe("int");
        GetRenderedPropertyType(props, "LongProp").ShouldBe("long");
        GetRenderedPropertyType(props, "UShortProp").ShouldBe("ushort");
        GetRenderedPropertyType(props, "UIntProp").ShouldBe("uint");
        GetRenderedPropertyType(props, "ULongProp").ShouldBe("ulong");
        GetRenderedPropertyType(props, "ObjectProp").ShouldBe("object");
        GetRenderedPropertyType(props, "StringProp").ShouldBe("string");
        GetRenderedPropertyType(props, "NIntProp").ShouldBe("nint");
        GetRenderedPropertyType(props, "NUIntProp").ShouldBe("nuint");
    }

    // === Array Tests ===

    [Fact]
    public void RenderPropertySignature_IntArray()
    {
        var prop = GetTestType("ArrayTestClass").GetProperty("IntArray")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public int[] IntArray { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_StringArray()
    {
        var prop = GetTestType("ArrayTestClass").GetProperty("StringArray")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public string[] StringArray { get; set; }");
    }

    [Fact]
    public void RenderPropertySignature_MultiDimArray()
    {
        var prop = GetTestType("ArrayTestClass").GetProperty("MultiDimArray")!;
        var result = TypeRenderer.RenderPropertySignature(prop);
        result.ShouldBe("public int[,] MultiDimArray { get; set; }");
    }

    // === RenderTypeName Direct Tests ===

    [Fact]
    public void RenderTypeName_String()
    {
        var type = _mlc.CoreAssembly!.GetType("System.String")!;
        var result = TypeRenderer.RenderTypeName(type);
        result.ShouldBe("string");
    }

    [Fact]
    public void RenderTypeName_Int32()
    {
        var type = _mlc.CoreAssembly!.GetType("System.Int32")!;
        var result = TypeRenderer.RenderTypeName(type);
        result.ShouldBe("int");
    }

    [Fact]
    public void RenderTypeName_Void()
    {
        var type = _mlc.CoreAssembly!.GetType("System.Void")!;
        var result = TypeRenderer.RenderTypeName(type);
        result.ShouldBe("void");
    }

    // === Tests against runtime assemblies ===

    [Fact]
    public void RenderTypeDeclaration_RuntimeEnum_StringComparison()
    {
        var type = _mlc.CoreAssembly!.GetType("System.StringComparison")!;
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public enum StringComparison");
    }

    [Fact]
    public void RenderTypeDeclaration_RuntimeInterface_IDisposable()
    {
        var type = _mlc.CoreAssembly!.GetType("System.IDisposable")!;
        var result = TypeRenderer.RenderTypeDeclaration(type);
        result.ShouldBe("public interface IDisposable");
    }

    private static string GetRenderedPropertyType(PropertyInfo[] props, string name)
    {
        var prop = props.First(p => p.Name == name);
        return TypeRenderer.RenderTypeName(prop.PropertyType);
    }
}
