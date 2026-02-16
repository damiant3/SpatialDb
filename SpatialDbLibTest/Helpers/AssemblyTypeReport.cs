using SpatialDbLib.Lattice;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
///////////////////////////
namespace SpatialDbLibTest.Helpers;

[TestClass]
public class AssemblyTypeReport
{
    public static string ReportAssembly(Type exampleType)
    {
        var assembly = exampleType.Assembly;
        return ReportAssembly(assembly);
    }
    public static string ReportAssembly(Assembly assembly)
    {

        var sb = new StringBuilder();
        foreach (var type in assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("SpatialDbLib") == true)
            .Where(ShouldReportType)
            .OrderBy(t => t.FullName))
        {
            sb.Append(ReportType(type));
        }
        return sb.ToString();
    }

    [TestMethod]
    public void AssemblyTypeReport_GenerateReport()
    {
        var report = ReportAssembly(typeof(SpatialLattice));
        Console.WriteLine(report);
    }

    public enum Accessibility
    {
        Private,
        PrivateProtected,
        Protected,
        Internal,
        ProtectedInternal,
        Public
    }

    static Accessibility GetMethodAccessibility(MethodBase method)
    {
        if (method.IsPublic) return Accessibility.Public;
        if (method.IsFamilyAndAssembly) return Accessibility.PrivateProtected;
        if (method.IsFamily) return Accessibility.Protected;
        if (method.IsFamilyOrAssembly) return Accessibility.ProtectedInternal;
        if (method.IsAssembly) return Accessibility.Internal;
        return Accessibility.Private;
    }

    static Accessibility GetFieldAccessibility(FieldInfo field)
    {
        if (field.IsPublic) return Accessibility.Public;
        if (field.IsFamilyAndAssembly) return Accessibility.PrivateProtected;
        if (field.IsFamily) return Accessibility.Protected;
        if (field.IsFamilyOrAssembly) return Accessibility.ProtectedInternal;
        if (field.IsAssembly) return Accessibility.Internal;
        return Accessibility.Private;
    }

    static Accessibility GetTypeAccessibility(Type type)
    {
        if (type.IsNested)
        {
            if (type.IsNestedPublic) return Accessibility.Public;
            if (type.IsNestedAssembly) return Accessibility.Internal;
            if (type.IsNestedFamily) return Accessibility.Protected;
            if (type.IsNestedFamORAssem) return Accessibility.ProtectedInternal;
            if (type.IsNestedFamANDAssem) return Accessibility.PrivateProtected;
            return Accessibility.Private;
        }

        if (type.IsPublic) return Accessibility.Public;
        if (type.IsNotPublic) return Accessibility.Internal;

        throw new InvalidOperationException($"Unknown accessibility for type {type.FullName}");
    }

    static Accessibility GetPropertyAccessibility(PropertyInfo property)
    {
        var accessors = property.GetAccessors(nonPublic: true);

        if (accessors.Length == 0)
            return Accessibility.Private;

        // Most permissive accessor defines effective visibility
        return accessors
            .Select(GetMethodAccessibility)
            .Max();
    }

    public static string ReportType(Type type, int indent = 0)
    {
        if (!ShouldReportType(type)) return "";

        var pad = new string(' ', indent * 4);

        var sb = new StringBuilder();
        sb.Append($"{GetKind(type)} ");
        sb.Append($"{GetTypeAccessibility(type).ToString().ToLower()}");

        sb.Append($" {type.FullName}");
        sb.AppendLine();

        const BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        foreach (var ctor in type.GetConstructors(flags))
        {
            if (!ShouldReportMember(ctor)) continue;
            sb.AppendLine($"{pad}{pad}constructor {GetMethodAccessibility(ctor)} ({ctor.GetParameters().Length})");
        }

        foreach (var field in type.GetFields(flags))
        {
            if (!ShouldReportMember(field)) continue;
            sb.AppendLine($"{pad}{pad}field {GetFieldAccessibility(field)} {field.Name} {field.FieldType.Name}");
        }

        foreach (var prop in type.GetProperties(flags))
        {
            if (!ShouldReportMember(prop)) continue;
            sb.AppendLine($"{pad}{pad}property {GetPropertyAccessibility(prop)} {prop.Name} {prop.PropertyType.Name}");
        }

        foreach (var method in type.GetMethods(flags))
        {
            if (!ShouldReportMember(method)) continue;
            if (method.IsSpecialName) continue;
            sb.AppendLine(
                $"{pad}{pad}method {GetMethodAccessibility(method)} {method.Name}() {method.ReturnType.Name}");
        }

        var nested = type.GetNestedTypes(flags)
            .Where(ShouldReportType)
            .OrderBy(t => t.Name)
            .ToList();

        if (nested.Count > 0)
        {
            sb.AppendLine($"{pad}{pad}nested:");
            foreach (var n in nested)
                ReportType(n, indent + 2);
        }
        return sb.ToString();
    }
    static string GetKind(Type type)
    {
        if (type.IsInterface)
            return "interface";

        if (type.IsEnum)
            return "enum";

        if (type.IsValueType)
        {
            if (IsRecord(type))
                return "struct record";

            return "struct";
        }

        // Classes
        if (IsRecord(type))
            return "class record";

        if (type.IsAbstract && type.IsSealed)
            return "class static";

        if (type.IsAbstract)
            return "class abstract";

        if (type.IsSealed)
            return "class sealed";

        return "class";
    }

    static bool IsRecord(Type type)
    {
        // C# compiler emits a protected virtual PrintMembers method for records
        return type.GetMethod(
            "PrintMembers",
            BindingFlags.Instance | BindingFlags.NonPublic
        ) != null;
    }

    static bool IsCompilerGenerated(MemberInfo member)
    {
        // Direct attribute (most reliable)
        if (member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            return true;

        // Common compiler-generated name patterns
        // <>c, <>c__DisplayClass*, <Method>b__*, <Method>g__*
        var name = member.Name;

        if (name.Length > 0 && name[0] == '<')
            return true;

        // Async / iterator state machines (important edge case)
        if (member is Type t)
        {
            if (typeof(IAsyncStateMachine).IsAssignableFrom(t))
                return true;

            if (typeof(System.Collections.IEnumerator).IsAssignableFrom(t)
                && t.Name.Contains("d__", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    static bool IsRelevant(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Private => true,

            // PrivateProtected is still visible across the hierarchy
            // and should be reviewed in encapsulation passes
            Accessibility.PrivateProtected => true,

            Accessibility.Protected => true,
            Accessibility.Internal => true,
            Accessibility.ProtectedInternal => true,
            Accessibility.Public => true,

            _ => false
        };
    }

    static bool ShouldReportType(Type t)
    {
        if (IsCompilerGenerated(t)) return false;
        if (t.IsNestedPrivate) return false;
        return true;
    }

    static bool ShouldReportMember(MemberInfo m)
    {
        if (IsCompilerGenerated(m)) return false;

        return m switch
        {
            MethodBase mb => IsRelevant(GetMethodAccessibility(mb)),
            PropertyInfo p => IsRelevant(GetPropertyAccessibility(p)),
            FieldInfo f => IsRelevant(GetFieldAccessibility(f)),
            _ => false
        };
    }



}
