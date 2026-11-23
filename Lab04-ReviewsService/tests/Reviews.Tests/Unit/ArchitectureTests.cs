using System.Reflection;
using MongoDB.Driver;
using Reviews.Application.Common.Messaging;
using Reviews.Domain.Common;

namespace Reviews.Tests.Unit;

/// <summary>Правило залежностей Clean Architecture, перевірене на скомпільованих збірках.</summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(BaseEntity).Assembly;
    private static readonly Assembly Application = typeof(ICommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(Reviews.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly WebApi = typeof(Program).Assembly;

    private static IEnumerable<string> References(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!);

    [Fact]
    public void Domain_references_no_other_project_and_no_mongodb_driver()
    {
        var refs = References(Domain).ToList();
        Assert.DoesNotContain(refs, r => r.StartsWith("Reviews.", StringComparison.Ordinal));
        Assert.DoesNotContain("MongoDB.Driver", refs);
    }

    [Fact]
    public void Application_depends_only_on_domain()
    {
        var refs = References(Application).ToList();
        Assert.Contains("Reviews.Domain", refs);
        Assert.DoesNotContain("Reviews.Infrastructure", refs);
        Assert.DoesNotContain("Reviews.WebApi", refs);
        Assert.DoesNotContain("MongoDB.Driver", refs);
    }

    [Fact]
    public void Infrastructure_does_not_reference_webapi()
        => Assert.DoesNotContain("Reviews.WebApi", References(Infrastructure));

    [Fact]
    public void No_controller_or_handler_uses_IMongoCollection_directly()
    {
        static bool UsesMongo(Type t) =>
            t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => IsMongo(p.ParameterType))
            || t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Any(f => IsMongo(f.FieldType));

        // доступ до даних: колекції, база, клієнт, контекст (винятки драйвера в обробнику помилок — не доступ до даних)
        static bool IsMongo(Type t) =>
            (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IMongoCollection<>))
            || t == typeof(IMongoDatabase) || t == typeof(IMongoClient)
            || t.Name.StartsWith("MongoDbContext", StringComparison.Ordinal);

        var offenders = Application.GetTypes().Concat(WebApi.GetTypes())
            .Where(t => t.IsClass && UsesMongo(t))
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Domain_classes_have_no_bson_attributes()
    {
        var withAttributes = Domain.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(true).Concat(t.GetMembers().SelectMany(m => m.GetCustomAttributes(true))))
            .Where(a => a.GetType().Namespace?.StartsWith("MongoDB.Bson.Serialization.Attributes", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Empty(withAttributes);
    }
}
