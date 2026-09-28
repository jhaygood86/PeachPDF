using System.Reflection;
using System.Text;

namespace PeachDrawing.Core.Tests.PublicApi
{
    /// <summary>
    /// Pins the public surface of <c>PeachDrawing.Core</c> to
    /// <c>src/PeachDrawing.Core/PublicApi.txt</c>, so a type or member becomes public only by a
    /// reviewed edit to that file. Run the tests with <c>UPDATE_PUBLIC_API=1</c> to rewrite it after an
    /// intended change. Adapted from <c>PeachDrawing.Text.Tests</c>'s own
    /// <c>PublicApi/PublicSurfaceTests.cs</c> - identical reflection-based snapshot logic, but see
    /// <see cref="TheGrantedInternalsAreExactlyTheDocumentedSet"/> for how the InternalsVisibleTo
    /// assertion differs.
    /// </summary>
    public class PublicSurfaceTests
    {
        private const string SnapshotFile = "PublicApi.txt";

        [Fact]
        public void PublicSurface_MatchesTheCheckedInSnapshot()
        {
            var actual = Describe(typeof(Canvas).Assembly);
            var sourcePath = FindSnapshotInSource();

            if (Environment.GetEnvironmentVariable("UPDATE_PUBLIC_API") == "1")
            {
                File.WriteAllText(sourcePath, actual);
                return;
            }

            var expected = File.ReadAllText(sourcePath).Replace("\r\n", "\n");
            Assert.True(expected == actual,
                "The public surface of PeachDrawing.Core changed. If that is intended, review the difference and rerun with UPDATE_PUBLIC_API=1 to rewrite "
                + SnapshotFile + ".\n--- actual ---\n" + actual);
        }

        /// <summary>
        /// Unlike <c>PeachDrawing.Text</c> (which grants its internals to no assembly but its own tests),
        /// this project deliberately grants PeachPDF and PeachPDF.Tests access too - see the
        /// InternalsVisibleTo comment in <c>PeachDrawing.Core.csproj</c> for the full reasoning
        /// (a settled, permanent design choice, not a transitional one). This test pins the granted set to
        /// exactly those three, so a future accidental widening (granting to some unrelated project) still
        /// fails a test instead of silently expanding the surface PeachPDF-adjacent code can reach.
        /// </summary>
        [Fact]
        public void TheGrantedInternalsAreExactlyTheDocumentedSet()
        {
            var granted = typeof(Canvas).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
                .Select(a => a.AssemblyName)
                .ToList();

            Assert.Equivalent(new[] { "PeachDrawing.Core.Tests", "PeachPDF", "PeachPDF.Tests" }, granted);
        }

        private static string Describe(Assembly assembly)
        {
            var text = new StringBuilder();
            foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                text.Append(type.FullName).Append(" : ").AppendLine(Kind(type));

                var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(Member)
                    .Where(s => s.Length > 0)
                    .OrderBy(s => s, StringComparer.Ordinal);

                foreach (var member in members)
                {
                    text.Append("  ").AppendLine(member);
                }
            }

            // The snapshot is stored with LF line endings whatever the platform's own are.
            return text.ToString().Replace("\r\n", "\n");
        }

        private static bool IsInitOnly(MethodInfo setter)
            => setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        private static string Kind(Type type)
        {
            if (type.IsEnum) return "enum " + Enum.GetUnderlyingType(type).Name;
            if (type.IsInterface) return "interface";
            if (type.IsValueType) return "struct";
            if (type.IsAbstract && type.IsSealed) return "static class";
            return type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";
        }

        private static string Member(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo f when f.DeclaringType!.IsEnum:
                    return f.IsSpecialName ? "" : f.Name + " = " + Convert.ToInt64(f.GetRawConstantValue());
                case FieldInfo f:
                    return "field " + (f.IsLiteral ? "const " : f.IsStatic ? "static " : "") + Name(f.FieldType) + " " + f.Name;
                case PropertyInfo p:
                    return "property " + (p.GetMethod?.IsStatic == true ? "static " : "") + Name(p.PropertyType) + " " + p.Name
                        + (p.GetIndexParameters().Length > 0 ? "[" + Parameters(p.GetIndexParameters()) + "]" : "")
                        + " {" + (p.GetMethod?.IsPublic == true ? " get;" : "") + (p.SetMethod?.IsPublic == true ? (IsInitOnly(p.SetMethod) ? " init;" : " set;") : "") + " }";
                case ConstructorInfo c:
                    return "ctor(" + Parameters(c.GetParameters()) + ")";
                case MethodInfo m when !m.IsSpecialName:
                    return "method " + (m.IsStatic ? "static " : m.IsAbstract ? "abstract " : m.IsVirtual ? "virtual " : "") + Name(m.ReturnType) + " " + m.Name
                        + (m.IsGenericMethod ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "")
                        + "(" + Parameters(m.GetParameters()) + ")";
                default:
                    return "";
            }
        }

        private static string Parameters(ParameterInfo[] parameters)
            => string.Join(", ", parameters.Select(p => (p.IsOut ? "out " : p.IsIn ? "in " : p.ParameterType.IsByRef ? "ref " : "") + Name(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : "")));

        private static string Name(Type type)
        {
            if (type.IsByRef) return Name(type.GetElementType()!);
            if (type.IsArray) return Name(type.GetElementType()!) + "[]";
            if (Nullable.GetUnderlyingType(type) is { } underlying) return Name(underlying) + "?";
            if (!type.IsGenericType) return type.Name;
            var tick = type.Name.IndexOf('`');
            return type.Name[..tick] + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
        }

        private static string FindSnapshotInSource()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "PeachDrawing.Core", SnapshotFile);
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException("Could not find " + SnapshotFile + " above " + AppContext.BaseDirectory);
        }
    }
}
