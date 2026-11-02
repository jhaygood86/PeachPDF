using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PeachBrackets.Android.Tests.PublicApi;

/// <summary>
/// Pins the public surface of <c>PeachBrackets.Android</c> to <c>src/engine/PeachBrackets.Android/PublicApi.txt</c>. The assembly targets Android, so it
/// cannot be loaded by a plain .NET test; it is read as metadata instead, which needs no Android assemblies. The test reads the build of the project
/// that is on disk (build it first, with the android workload) and is skipped when there is none. Run with <c>UPDATE_PUBLIC_API=1</c> to rewrite the
/// snapshot after an intended change.
/// </summary>
public class PublicSurfaceTests
{
    private const string SnapshotFile = "PublicApi.txt";

    [Fact]
    public void PublicSurface_MatchesTheCheckedInSnapshot()
    {
        var source = FindProjectDirectory();
        var bin = Path.Combine(source, "bin");
        var assembly = Directory.Exists(bin)
            ? Directory.EnumerateFiles(bin, "PeachBrackets.Android.dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        Assert.SkipWhen(assembly is null, "PeachBrackets.Android has not been built (it needs the android workload).");

        var actual = Describe(assembly!);
        var snapshot = Path.Combine(source, SnapshotFile);
        if (Environment.GetEnvironmentVariable("UPDATE_PUBLIC_API") == "1")
        {
            File.WriteAllText(snapshot, actual);
            return;
        }

        var expected = File.ReadAllText(snapshot).Replace("\r\n", "\n");
        Assert.True(expected == actual,
            "The public surface of PeachBrackets.Android changed. If that is intended, review the difference and rerun with UPDATE_PUBLIC_API=1 to rewrite "
            + SnapshotFile + ".\n--- actual ---\n" + actual);
    }

    private static bool Visible(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    private static string Describe(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new TypeNames(reader);
        var text = new StringBuilder();

        foreach (var type in reader.TypeDefinitions.Select(reader.GetTypeDefinition).Where(t => IsExported(reader, t)).OrderBy(t => names.Of(t), StringComparer.Ordinal))
        {
            text.Append(names.Of(type)).Append(" : ").AppendLine(Kind(reader, type));

            var members = new List<string>();
            foreach (var handle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(handle);
                var access = field.Attributes & FieldAttributes.FieldAccessMask;
                if (access is not (FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem) || field.Attributes.HasFlag(FieldAttributes.SpecialName))
                {
                    continue;
                }

                var literal = field.Attributes.HasFlag(FieldAttributes.Literal);
                var isStatic = field.Attributes.HasFlag(FieldAttributes.Static);
                members.Add(IsEnum(reader, type)
                    ? reader.GetString(field.Name) + " = " + EnumValue(reader, field)
                    : "field " + (literal ? "const " : isStatic ? "static " : "") + field.DecodeSignature(names, null) + " " + reader.GetString(field.Name));
            }

            foreach (var handle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                var name = reader.GetString(method.Name);
                if (!Visible(method.Attributes) || (method.Attributes.HasFlag(MethodAttributes.SpecialName) && !name.StartsWith('.')))
                {
                    continue;
                }

                var signature = method.DecodeSignature(names, null);
                var parameterNames = method.GetParameters().Select(reader.GetParameter).Where(p => p.SequenceNumber > 0).OrderBy(p => p.SequenceNumber).Select(p => reader.GetString(p.Name)).ToList();
                var list = string.Join(", ", signature.ParameterTypes.Select((t, i) => t + " " + (i < parameterNames.Count ? parameterNames[i] : "_")));
                members.Add(name.StartsWith('.')
                    ? "ctor(" + list + ")"
                    : "method " + (method.Attributes.HasFlag(MethodAttributes.Static) ? "static " : method.Attributes.HasFlag(MethodAttributes.Abstract) ? "abstract " : method.Attributes.HasFlag(MethodAttributes.Virtual) ? "virtual " : "")
                      + signature.ReturnType + " " + name + "(" + list + ")");
            }

            foreach (var handle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(handle);
                var accessors = property.GetAccessors();
                MethodDefinition? getter = accessors.Getter.IsNil ? null : reader.GetMethodDefinition(accessors.Getter);
                MethodDefinition? setter = accessors.Setter.IsNil ? null : reader.GetMethodDefinition(accessors.Setter);
                var canGet = getter is { } g && Visible(g.Attributes);
                var canSet = setter is { } s && Visible(s.Attributes);
                if (!canGet && !canSet)
                {
                    continue;
                }

                var isStatic = (getter ?? setter)!.Value.Attributes.HasFlag(MethodAttributes.Static);
                members.Add("property " + (isStatic ? "static " : "") + property.DecodeSignature(names, null).ReturnType + " " + reader.GetString(property.Name)
                    + " {" + (canGet ? " get;" : "") + (canSet ? " set;" : "") + " }");
            }

            foreach (var handle in type.GetEvents())
            {
                var evt = reader.GetEventDefinition(handle);
                members.Add("event " + names.GetTypeFromHandle(reader, null, evt.Type) + " " + reader.GetString(evt.Name));
            }

            foreach (var member in members.OrderBy(m => m, StringComparer.Ordinal))
            {
                text.Append("  ").AppendLine(member);
            }
        }

        return text.ToString().Replace("\r\n", "\n");
    }

    private static bool IsExported(MetadataReader reader, TypeDefinition type)
    {
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public)
        {
            return true;
        }

        return visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
            && IsExported(reader, reader.GetTypeDefinition(type.GetDeclaringType()));
    }

    private static string BaseName(MetadataReader reader, TypeDefinition type) =>
        !type.BaseType.IsNil && type.BaseType.Kind == HandleKind.TypeReference ? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)type.BaseType).Name) : "";

    private static bool IsEnum(MetadataReader reader, TypeDefinition type) => BaseName(reader, type) == "Enum";

    private static string Kind(MetadataReader reader, TypeDefinition type)
    {
        if (type.Attributes.HasFlag(TypeAttributes.Interface)) return "interface";
        if (IsEnum(reader, type)) return "enum";
        if (BaseName(reader, type) == "ValueType") return "struct";
        if (type.Attributes.HasFlag(TypeAttributes.Abstract) && type.Attributes.HasFlag(TypeAttributes.Sealed)) return "static class";
        return type.Attributes.HasFlag(TypeAttributes.Abstract) ? "abstract class" : type.Attributes.HasFlag(TypeAttributes.Sealed) ? "sealed class" : "class";
    }

    private static string EnumValue(MetadataReader reader, FieldDefinition field)
    {
        if (field.GetDefaultValue().IsNil)
        {
            return "?";
        }

        var constant = reader.GetConstant(field.GetDefaultValue());
        return Convert.ToInt64(reader.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode)).ToString();
    }

    private static string FindProjectDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "engine", "PeachBrackets.Android");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not find engine/PeachBrackets.Android above " + AppContext.BaseDirectory);
    }

    // Writes the types a signature mentions by name; nothing is resolved, which is what lets Android types appear in a plain test.
    private sealed class TypeNames(MetadataReader reader) : ISignatureTypeProvider<string, object?>
    {
        public string Of(TypeDefinition type)
        {
            var name = reader.GetString(type.Name);
            if (!type.GetDeclaringType().IsNil)
            {
                return Of(reader.GetTypeDefinition(type.GetDeclaringType())) + "+" + name;
            }

            var ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind) => r.GetString(r.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind) => r.GetString(r.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(MetadataReader r, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            r.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

        public string GetByReferenceType(string elementType) => elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            var tick = genericType.IndexOf('`');
            return (tick < 0 ? genericType : genericType[..tick]) + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetTypeFromHandle(MetadataReader r, object? genericContext, EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(r, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(r, (TypeReferenceHandle)handle, 0),
            _ => GetTypeFromSpecification(r, genericContext, (TypeSpecificationHandle)handle, 0),
        };
    }
}
