using System.Reflection;

namespace SharpRecon.Inspection;

public static class TypeRenderer
{
    private static readonly Dictionary<string, string> BuiltInAliases = new()
    {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Decimal"] = "decimal",
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Int16"] = "short",
        ["System.Int32"] = "int",
        ["System.Int64"] = "long",
        ["System.UInt16"] = "ushort",
        ["System.UInt32"] = "uint",
        ["System.UInt64"] = "ulong",
        ["System.Object"] = "object",
        ["System.String"] = "string",
        ["System.Void"] = "void",
        ["System.IntPtr"] = "nint",
        ["System.UIntPtr"] = "nuint",
    };

    public static string RenderTypeDeclaration(Type type)
    {
        var parts = new List<string>();

        parts.Add(GetAccessModifier(type));

        if (type.IsEnum)
        {
            parts.Add("enum");
            parts.Add(RenderSimpleTypeName(type));
            var underlyingType = type.GetEnumUnderlyingType();
            if (underlyingType.FullName != "System.Int32")
            {
                parts.Add(":");
                parts.Add(RenderTypeName(underlyingType));
            }
            return string.Join(" ", parts);
        }

        if (IsDelegate(type))
        {
            return RenderDelegateDeclaration(type);
        }

        var isRecord = IsRecord(type);
        var isRecordStruct = isRecord && type.IsValueType;

        if (type.IsAbstract && type.IsSealed)
            parts.Add("static");
        else if (type.IsAbstract && !type.IsInterface)
            parts.Add("abstract");
        else if (type.IsSealed && !type.IsValueType && !isRecord)
            parts.Add("sealed");

        if (type.IsValueType && IsReadOnlyStruct(type))
            parts.Add("readonly");

        if (isRecord && isRecordStruct)
            parts.Add("record struct");
        else if (isRecord)
            parts.Add("record");
        else if (type.IsInterface)
            parts.Add("interface");
        else if (type.IsValueType)
        {
            if (IsByRefLikeStruct(type))
                parts.Add("ref struct");
            else
                parts.Add("struct");
        }
        else
            parts.Add("class");

        parts.Add(RenderSimpleTypeName(type));

        var baseTypes = new List<string>();

        if (!type.IsInterface && !type.IsValueType && type.BaseType is not null)
        {
            var baseName = type.BaseType.FullName;
            if (baseName != "System.Object" && baseName != "System.ValueType")
            {
                baseTypes.Add(RenderTypeName(type.BaseType));
            }
        }

        var interfaces = GetDirectlyImplementedInterfaces(type);
        foreach (var iface in interfaces)
        {
            baseTypes.Add(RenderTypeName(iface));
        }

        if (baseTypes.Count > 0)
        {
            parts.Add(":");
            parts.Add(string.Join(", ", baseTypes));
        }

        var constraints = RenderGenericConstraints(type.GetGenericArguments());
        if (!string.IsNullOrEmpty(constraints))
            parts.Add(constraints);

        return string.Join(" ", parts);
    }

    public static string RenderMethodSignature(MethodInfo method)
    {
        var parts = new List<string>();

        parts.Add(GetMethodAccessModifier(method));

        if (method.IsStatic)
            parts.Add("static");

        if (method.IsAbstract)
            parts.Add("abstract");
        else if (method.IsFinal && IsOverride(method))
            parts.Add("sealed override");
        else if (IsOverride(method))
            parts.Add("override");
        else if (method.IsVirtual && !method.IsFinal)
            parts.Add("virtual");

        var nullCtx = GetNullableContext(method) ?? GetNullableContext(method.DeclaringType!);
        var returnNullable = GetNullableAttribute(method.ReturnTypeCustomAttributes);

        parts.Add(RenderTypeName(method.ReturnType, returnNullable, nullCtx));
        parts.Add(RenderMethodName(method) + RenderParameters(method.GetParameters(), nullCtx));

        var constraints = RenderGenericConstraints(method.GetGenericArguments());
        if (!string.IsNullOrEmpty(constraints))
            parts.Add(constraints);

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    public static string RenderConstructorSignature(ConstructorInfo ctor)
    {
        var parts = new List<string>();

        parts.Add(GetMethodAccessModifier(ctor));

        if (ctor.IsStatic)
            parts.Add("static");

        var declaringType = ctor.DeclaringType!;
        var typeName = declaringType.Name;
        var backtickIndex = typeName.IndexOf('`');
        if (backtickIndex >= 0)
            typeName = typeName[..backtickIndex];

        var nullCtx = GetNullableContext(ctor) ?? GetNullableContext(declaringType);

        parts.Add(typeName + RenderParameters(ctor.GetParameters(), nullCtx));

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    public static string RenderPropertySignature(PropertyInfo property)
    {
        var parts = new List<string>();

        var getter = property.GetGetMethod(true);
        var setter = property.GetSetMethod(true);
        var accessor = getter ?? setter;

        if (accessor is not null)
            parts.Add(GetMethodAccessModifier(accessor));

        if (accessor?.IsStatic == true)
            parts.Add("static");

        if (accessor is not null && !accessor.IsStatic)
        {
            if (accessor.IsAbstract)
                parts.Add("abstract");
            else if (accessor.IsFinal && IsOverride(accessor))
                parts.Add("sealed override");
            else if (IsOverride(accessor))
                parts.Add("override");
            else if (accessor.IsVirtual && !accessor.IsFinal)
                parts.Add("virtual");
        }

        var nullCtx = GetNullableContext(property) ?? GetNullableContext(property.DeclaringType!);
        var nullable = GetNullableAttribute(property);

        parts.Add(RenderTypeName(property.PropertyType, nullable, nullCtx));

        var indexParams = property.GetIndexParameters();
        if (indexParams.Length > 0)
        {
            parts.Add("this" + RenderParameters(indexParams, nullCtx, "[", "]"));
        }
        else
        {
            parts.Add(property.Name);
        }

        parts.Add(RenderPropertyAccessors(property));

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    public static string RenderFieldSignature(FieldInfo field)
    {
        var parts = new List<string>();

        parts.Add(GetFieldAccessModifier(field));

        if (field.IsLiteral)
            parts.Add("const");
        else if (field.IsStatic)
            parts.Add("static");

        if (field.IsInitOnly)
            parts.Add("readonly");

        var nullCtx = GetNullableContext(field) ?? GetNullableContext(field.DeclaringType!);
        var nullable = GetNullableAttribute(field);

        parts.Add(RenderTypeName(field.FieldType, nullable, nullCtx));
        parts.Add(field.Name);

        if (field.IsLiteral)
        {
            var rawValue = field.GetRawConstantValue();
            parts.Add("=");
            parts.Add(RenderConstantValue(rawValue, field.FieldType));
        }

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    public static string RenderEventSignature(EventInfo evt)
    {
        var parts = new List<string>();

        var addMethod = evt.GetAddMethod(true);
        if (addMethod is not null)
            parts.Add(GetMethodAccessModifier(addMethod));

        if (addMethod?.IsStatic == true)
            parts.Add("static");

        if (addMethod is not null && !addMethod.IsStatic)
        {
            if (addMethod.IsAbstract)
                parts.Add("abstract");
            else if (addMethod.IsVirtual && !addMethod.IsFinal)
                parts.Add("virtual");
        }

        parts.Add("event");

        var nullCtx = GetNullableContext(evt) ?? GetNullableContext(evt.DeclaringType!);
        var nullable = GetNullableAttribute(evt);

        parts.Add(RenderTypeName(evt.EventHandlerType!, nullable, nullCtx));
        parts.Add(evt.Name);

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    public static string RenderTypeName(Type type, byte[]? nullable = null, byte? nullableContext = null)
    {
        return RenderTypeNameInternal(type, nullable, ref nullableContext, index: 0).rendered;
    }

    private static (string rendered, int nextIndex) RenderTypeNameInternal(
        Type type, byte[]? nullable, ref byte? nullableContext, int index)
    {
        if (type.IsByRef)
        {
            var result = RenderTypeNameInternal(type.GetElementType()!, nullable, ref nullableContext, index);
            return (result.rendered, result.nextIndex);
        }

        if (type.IsPointer)
        {
            var result = RenderTypeNameInternal(type.GetElementType()!, nullable, ref nullableContext, index);
            return (result.rendered + "*", result.nextIndex);
        }

        if (type.IsArray)
        {
            var annotationByte = GetNullableByte(nullable, nullableContext, index);
            index++;

            var elementResult = RenderTypeNameInternal(type.GetElementType()!, nullable, ref nullableContext, index);

            var rank = type.GetArrayRank();
            var arraySuffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";

            var rendered = elementResult.rendered + arraySuffix;
            if (annotationByte == 2)
                rendered += "?";

            return (rendered, elementResult.nextIndex);
        }

        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var fullName = genericDef.FullName ?? genericDef.Name;

            if (fullName.StartsWith("System.Nullable`1"))
            {
                var args = type.GetGenericArguments();
                var innerResult = RenderTypeNameInternal(args[0], nullable, ref nullableContext, index);
                return (innerResult.rendered + "?", innerResult.nextIndex);
            }

            var annotationByte = GetNullableByte(nullable, nullableContext, index);
            index++;

            var backtickIdx = fullName.IndexOf('`');
            var baseName = backtickIdx >= 0 ? fullName[..backtickIdx] : fullName;

            if (BuiltInAliases.TryGetValue(baseName, out var alias))
                baseName = alias;
            else
                baseName = SimplifyNamespace(baseName);

            var genericArgs = type.GetGenericArguments();
            var argRenderings = new List<string>();
            var currentIndex = index;

            foreach (var arg in genericArgs)
            {
                var argResult = RenderTypeNameInternal(arg, nullable, ref nullableContext, currentIndex);
                argRenderings.Add(argResult.rendered);
                currentIndex = argResult.nextIndex;
            }

            var rendered = baseName + "<" + string.Join(", ", argRenderings) + ">";
            if (annotationByte == 2)
                rendered += "?";

            return (rendered, currentIndex);
        }

        {
            var annotationByte = GetNullableByte(nullable, nullableContext, index);

            var typeName = type.FullName ?? type.Name;
            if (BuiltInAliases.TryGetValue(typeName, out var alias))
                typeName = alias;
            else
                typeName = SimplifyNamespace(typeName);

            if (annotationByte == 2 && !type.IsValueType)
                typeName += "?";

            return (typeName, index + 1);
        }
    }

    private static byte GetNullableByte(byte[]? nullable, byte? nullableContext, int index)
    {
        if (nullable is not null)
        {
            if (nullable.Length == 1)
                return nullable[0];
            if (index < nullable.Length)
                return nullable[index];
        }

        return nullableContext ?? 0;
    }

    private static string SimplifyNamespace(string fullName)
    {
        var plusIndex = fullName.LastIndexOf('+');
        if (plusIndex >= 0)
        {
            var outerPart = SimplifyNamespace(fullName[..plusIndex]);
            var innerPart = fullName[(plusIndex + 1)..];
            return outerPart + "." + innerPart;
        }

        var dotIndex = fullName.LastIndexOf('.');
        if (dotIndex < 0)
            return fullName;

        return fullName[(dotIndex + 1)..];
    }

    private static string RenderSimpleTypeName(Type type)
    {
        var name = type.Name;
        var backtickIndex = name.IndexOf('`');
        if (backtickIndex >= 0)
            name = name[..backtickIndex];

        if (type.DeclaringType is not null)
        {
            var outerName = RenderSimpleTypeName(type.DeclaringType);
            name = outerName + "." + name;
        }

        if (type.IsGenericTypeDefinition || (type.IsGenericType && !type.IsConstructedGenericType))
        {
            var genArgs = type.GetGenericArguments();
            if (type.DeclaringType is not null)
            {
                var outerArgCount = type.DeclaringType.GetGenericArguments().Length;
                genArgs = genArgs[outerArgCount..];
            }

            if (genArgs.Length > 0)
            {
                var renderVariance = type.IsInterface || IsDelegate(type);
                name += "<" + string.Join(", ", genArgs.Select(a =>
                    renderVariance ? RenderGenericParamWithVariance(a) : a.Name)) + ">";
            }
        }

        return name;
    }

    private static string RenderGenericParamWithVariance(Type genericParam)
    {
        var attrs = genericParam.GenericParameterAttributes;
        var variance = attrs & GenericParameterAttributes.VarianceMask;

        return variance switch
        {
            GenericParameterAttributes.Covariant => "out " + genericParam.Name,
            GenericParameterAttributes.Contravariant => "in " + genericParam.Name,
            _ => genericParam.Name
        };
    }

    private static string RenderDelegateDeclaration(Type type)
    {
        var parts = new List<string>();
        parts.Add(GetAccessModifier(type));
        parts.Add("delegate");

        var invokeMethod = type.GetMethod("Invoke")!;
        var nullCtx = GetNullableContext(invokeMethod) ?? GetNullableContext(type);
        var returnNullable = GetNullableAttribute(invokeMethod.ReturnTypeCustomAttributes);

        parts.Add(RenderTypeName(invokeMethod.ReturnType, returnNullable, nullCtx));

        var name = type.Name;
        var backtickIndex = name.IndexOf('`');
        if (backtickIndex >= 0)
            name = name[..backtickIndex];

        var genArgs = type.GetGenericArguments();
        if (genArgs.Length > 0)
            name += "<" + string.Join(", ", genArgs.Select(RenderGenericParamWithVariance)) + ">";

        parts.Add(name + RenderParameters(invokeMethod.GetParameters(), nullCtx));

        var constraints = RenderGenericConstraints(genArgs);
        if (!string.IsNullOrEmpty(constraints))
            parts.Add(constraints);

        return string.Join(" ", parts);
    }

    private static string RenderMethodName(MethodInfo method)
    {
        var name = method.Name;

        if (method.IsGenericMethodDefinition || method.IsGenericMethod)
        {
            var genArgs = method.GetGenericArguments();
            if (genArgs.Length > 0)
                name += "<" + string.Join(", ", genArgs.Select(a => a.Name)) + ">";
        }

        return name;
    }

    private static string RenderParameters(ParameterInfo[] parameters, byte? nullCtx, string open = "(", string close = ")")
    {
        if (parameters.Length == 0)
            return open + close;

        var paramStrings = new List<string>();
        foreach (var param in parameters)
        {
            paramStrings.Add(RenderParameter(param, nullCtx));
        }

        return open + string.Join(", ", paramStrings) + close;
    }

    private static string RenderParameter(ParameterInfo param, byte? nullCtx)
    {
        var parts = new List<string>();

        if (IsParamsParameter(param))
            parts.Add("params");

        var nullable = GetNullableAttribute(param);

        if (param.ParameterType.IsByRef)
        {
            if (param.IsOut)
                parts.Add("out");
            else if (param.IsIn)
                parts.Add("in");
            else
                parts.Add("ref");
        }

        if (IsScopedParameter(param))
            parts.Insert(parts.Count > 0 && parts[^1] is "ref" or "in" or "out" ? parts.Count - 1 : 0, "scoped");

        parts.Add(RenderTypeName(param.ParameterType, nullable, nullCtx));
        parts.Add(param.Name ?? "");

        if (param.HasDefaultValue)
        {
            parts.Add("=");
            parts.Add(RenderConstantValue(param.RawDefaultValue, param.ParameterType));
        }

        return string.Join(" ", parts);
    }

    private static bool IsParamsParameter(ParameterInfo param)
    {
        return param.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.ParamArrayAttribute");
    }

    private static bool IsScopedParameter(ParameterInfo param)
    {
        return param.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.ScopedRefAttribute");
    }

    private static string RenderConstantValue(object? value, Type parameterType)
    {
        if (value is null)
            return "null";

        var actualType = parameterType.IsByRef ? parameterType.GetElementType()! : parameterType;

        if (actualType.IsEnum)
        {
            var enumName = RenderTypeName(actualType);
            var fields = actualType.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in fields)
            {
                var rawVal = field.GetRawConstantValue();
                if (rawVal is not null && rawVal.Equals(value))
                    return enumName + "." + field.Name;
            }
            return $"({enumName}){value}";
        }

        return value switch
        {
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            char c => "'" + c.ToString().Replace("\\", "\\\\").Replace("'", "\\'") + "'",
            bool b => b ? "true" : "false",
            float f => f switch
            {
                float.PositiveInfinity => "float.PositiveInfinity",
                float.NegativeInfinity => "float.NegativeInfinity",
                float.NaN => "float.NaN",
                _ => f.ToString("G") + "f"
            },
            double d => d switch
            {
                double.PositiveInfinity => "double.PositiveInfinity",
                double.NegativeInfinity => "double.NegativeInfinity",
                double.NaN => "double.NaN",
                _ => d.ToString("G") + "d"
            },
            decimal m => m.ToString("G") + "m",
            long l => l.ToString() + "L",
            ulong ul => ul.ToString() + "UL",
            uint ui => ui.ToString() + "U",
            _ => value.ToString() ?? "default"
        };
    }

    private static string RenderPropertyAccessors(PropertyInfo property)
    {
        var getter = property.GetGetMethod(true);
        var setter = property.GetSetMethod(true);

        var accessorParts = new List<string>();

        if (getter is not null && IsPublicOrProtected(getter))
        {
            var prefix = GetAccessorPrefix(getter, property);
            accessorParts.Add(prefix + "get;");
        }

        if (setter is not null && IsPublicOrProtected(setter))
        {
            var prefix = GetAccessorPrefix(setter, property);
            var isInit = IsInitOnlySetter(setter);
            accessorParts.Add(prefix + (isInit ? "init;" : "set;"));
        }

        return "{ " + string.Join(" ", accessorParts) + " }";
    }

    private static string GetAccessorPrefix(MethodInfo accessor, PropertyInfo property)
    {
        var primaryAccessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
        if (primaryAccessor is null)
            return "";

        var propertyAccess = GetMethodAccessModifier(primaryAccessor);
        var accessorAccess = GetMethodAccessModifier(accessor);

        if (accessorAccess != propertyAccess && accessorAccess.Length > 0)
            return accessorAccess + " ";

        return "";
    }

    private static bool IsInitOnlySetter(MethodInfo setter)
    {
        var returnParam = setter.ReturnParameter;
        if (returnParam is null)
            return false;

        return returnParam.GetRequiredCustomModifiers()
            .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    private static bool IsPublicOrProtected(MethodInfo method)
    {
        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

    private static string GetAccessModifier(Type type)
    {
        if (type.IsPublic || type.IsNestedPublic)
            return "public";
        if (type.IsNestedFamily)
            return "protected";
        if (type.IsNestedFamORAssem)
            return "protected internal";
        if (type.IsNestedAssembly)
            return "internal";
        if (type.IsNestedPrivate)
            return "private";
        return "internal";
    }

    private static string GetMethodAccessModifier(MethodBase method)
    {
        if (method.IsPublic)
            return "public";
        if (method.IsFamily)
            return "protected";
        if (method.IsFamilyOrAssembly)
            return "protected internal";
        if (method.IsAssembly)
            return "internal";
        if (method.IsFamilyAndAssembly)
            return "private protected";
        if (method.IsPrivate)
            return "private";
        return "";
    }

    private static string GetFieldAccessModifier(FieldInfo field)
    {
        if (field.IsPublic)
            return "public";
        if (field.IsFamily)
            return "protected";
        if (field.IsFamilyOrAssembly)
            return "protected internal";
        if (field.IsAssembly)
            return "internal";
        if (field.IsFamilyAndAssembly)
            return "private protected";
        if (field.IsPrivate)
            return "private";
        return "";
    }

    private static bool IsOverride(MethodInfo method)
    {
        if (!method.IsVirtual)
            return false;

        return (method.Attributes & MethodAttributes.NewSlot) == 0;
    }

    private static bool IsDelegate(Type type)
    {
        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (baseType.FullName == "System.MulticastDelegate" || baseType.FullName == "System.Delegate")
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool IsReadOnlyStruct(Type type)
    {
        return type.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

    private static bool IsByRefLikeStruct(Type type)
    {
        return type.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute");
    }

    private static bool IsRecord(Type type)
    {
        try
        {
            var cloneMethod = type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance);
            if (cloneMethod is not null)
                return true;

            if (type.IsValueType)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
                var printMembers = methods.FirstOrDefault(m =>
                    m.Name == "PrintMembers" &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.FullName == "System.Text.StringBuilder");

                if (printMembers is not null)
                {
                    var equalityOp = type.GetMethod("op_Equality",
                        BindingFlags.Public | BindingFlags.Static);
                    if (equalityOp is not null)
                        return true;
                }
            }
        }
        catch
        {
            // best-effort
        }

        return false;
    }

    private static Type[] GetDirectlyImplementedInterfaces(Type type)
    {
        var allInterfaces = type.GetInterfaces();
        var baseInterfaces = type.BaseType?.GetInterfaces() ?? [];

        var directInterfaces = new List<Type>();
        foreach (var iface in allInterfaces)
        {
            if (Array.IndexOf(baseInterfaces, iface) >= 0)
                continue;

            var isInherited = false;
            foreach (var other in allInterfaces)
            {
                if (other == iface)
                    continue;
                if (Array.IndexOf(baseInterfaces, other) >= 0)
                    continue;
                if (other.GetInterfaces().Any(i => i == iface))
                {
                    isInherited = true;
                    break;
                }
            }

            if (!isInherited)
                directInterfaces.Add(iface);
        }

        return directInterfaces.ToArray();
    }

    private static string RenderGenericConstraints(Type[] genericArgs)
    {
        if (genericArgs.Length == 0)
            return "";

        var clauses = new List<string>();

        foreach (var arg in genericArgs)
        {
            var constraints = new List<string>();
            var attrs = arg.GenericParameterAttributes;

            if ((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            {
                if ((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                    constraints.Add("class");
            }

            if ((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                constraints.Add("struct");

            var typeConstraints = arg.GetGenericParameterConstraints();
            foreach (var tc in typeConstraints)
            {
                if (tc.FullName == "System.ValueType" &&
                    (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                    continue;

                constraints.Add(RenderTypeName(tc));
            }

            if ((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
            {
                constraints.Add("new()");
            }

            if (constraints.Count > 0)
            {
                clauses.Add($"where {arg.Name} : {string.Join(", ", constraints)}");
            }
        }

        return string.Join(" ", clauses);
    }

    private static byte[]? GetNullableAttribute(ParameterInfo param)
    {
        return GetNullableAttributeFromCustomAttributes(param.CustomAttributes);
    }

    private static byte[]? GetNullableAttribute(PropertyInfo prop)
    {
        return GetNullableAttributeFromCustomAttributes(prop.CustomAttributes);
    }

    private static byte[]? GetNullableAttribute(FieldInfo field)
    {
        return GetNullableAttributeFromCustomAttributes(field.CustomAttributes);
    }

    private static byte[]? GetNullableAttribute(EventInfo evt)
    {
        return GetNullableAttributeFromCustomAttributes(evt.CustomAttributes);
    }

    private static byte[]? GetNullableAttribute(ICustomAttributeProvider provider)
    {
        try
        {
            var attrs = provider.GetCustomAttributes(false);
            foreach (var attr in attrs)
            {
                var attrType = attr.GetType();
                if (attrType.FullName == "System.Runtime.CompilerServices.NullableAttribute")
                {
                    var field = attrType.GetField("NullableFlags");
                    if (field?.GetValue(attr) is byte[] flags)
                        return flags;
                }
            }
        }
        catch
        {
            // MLC may not support GetCustomAttributes on return params
        }

        if (provider is ParameterInfo pi)
            return GetNullableAttributeFromCustomAttributes(pi.CustomAttributes);

        return null;
    }

    private static byte[]? GetNullableAttributeFromCustomAttributes(IEnumerable<CustomAttributeData> attrs)
    {
        foreach (var attr in attrs)
        {
            if (attr.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute")
            {
                if (attr.ConstructorArguments.Count > 0)
                {
                    var arg = attr.ConstructorArguments[0];
                    if (arg.Value is byte singleByte)
                        return [singleByte];

                    if (arg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> collection)
                        return collection.Select(a => (byte)a.Value!).ToArray();
                }
            }
        }
        return null;
    }

    private static byte? GetNullableContext(MemberInfo? member)
    {
        if (member is null)
            return null;

        IEnumerable<CustomAttributeData> attrs;
        try
        {
            attrs = member.CustomAttributes;
        }
        catch
        {
            return null;
        }

        foreach (var attr in attrs)
        {
            if (attr.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute")
            {
                if (attr.ConstructorArguments.Count > 0 && attr.ConstructorArguments[0].Value is byte b)
                    return b;
            }
        }

        if (member is Type type && type.DeclaringType is not null)
            return GetNullableContext(type.DeclaringType);

        return null;
    }

    private static byte? GetNullableContext(MethodBase method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute")
            {
                if (attr.ConstructorArguments.Count > 0 && attr.ConstructorArguments[0].Value is byte b)
                    return b;
            }
        }
        return null;
    }
}
