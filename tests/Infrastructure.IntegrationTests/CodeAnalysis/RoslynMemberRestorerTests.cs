using DevAgents.Application.Common.Models;
using DevAgents.Infrastructure.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.CodeAnalysis;

/// <summary>Kodlama ajanının değiştirirken sildiği üyelerin, dosyanın eski hâlinden geri eklenmesi.</summary>
public class RoslynMemberRestorerTests
{
    private const string Original = """
        namespace Shop.Application.Common.Interfaces;

        public interface IApplicationDbContext
        {
            DbSet<TodoList> TodoLists { get; }

            DbSet<TodoItem> TodoItems { get; }

            Task<int> SaveChangesAsync(CancellationToken cancellationToken);
        }

        """;

    // Ajanın çıktısı: yeni DbSet eklenmiş, var olanlar düşmüş.
    private const string Current = """
        namespace Shop.Application.Common.Interfaces;

        public interface IApplicationDbContext
        {
            DbSet<Hobby> Hobbies { get; }

            Task<int> SaveChangesAsync(CancellationToken cancellationToken);
        }

        """;

    private readonly RoslynMemberRestorer _restorer = new();

    [Test]
    public void ShouldPutBackOnlyTheMembersTheBuildIsLookingFor()
    {
        var result = _restorer.Restore(Original, Current,
            [new MissingMember("IApplicationDbContext", "TodoLists"), new MissingMember("IApplicationDbContext", "TodoItems")]);

        result.ShouldNotBeNull();
        result.RestoredMembers.ShouldBe(["IApplicationDbContext.TodoLists", "IApplicationDbContext.TodoItems"]);
        result.Content.ShouldBe("""
            namespace Shop.Application.Common.Interfaces;

            public interface IApplicationDbContext
            {
                DbSet<Hobby> Hobbies { get; }

                Task<int> SaveChangesAsync(CancellationToken cancellationToken);
                DbSet<TodoList> TodoLists { get; }

                DbSet<TodoItem> TodoItems { get; }
            }

            """);
        CSharpSyntaxTree.ParseText(result.Content).GetDiagnostics().ShouldBeEmpty();
    }

    [Test]
    public void ShouldRestoreAnImplementationTheClassLost()
    {
        const string original = "public class Db : IDb\n{\n    public DbSet<TodoList> TodoLists => Set<TodoList>();\n}\n";
        const string current = "public class Db : IDb\n{\n    public DbSet<Hobby> Hobbies => Set<Hobby>();\n}\n";

        var result = _restorer.Restore(original, current, [new MissingMember("Db", "TodoLists")]);

        result.ShouldNotBeNull().Content.ShouldBe(
            "public class Db : IDb\n{\n    public DbSet<Hobby> Hobbies => Set<Hobby>();\n    public DbSet<TodoList> TodoLists => Set<TodoList>();\n}\n");
    }

    [Test]
    public void ShouldNotTouchMembersThatStillExistOrNeverExisted()
    {
        // TodoLists hâlâ var (sorun silinme değil), Title hiç yoktu (ajanın yeni kodu uyduruyor): ikisi de düzeltme ajanına kalır.
        _restorer.Restore(Original, Original,
            [new MissingMember("IApplicationDbContext", "TodoLists"), new MissingMember("IApplicationDbContext", "Title")]).ShouldBeNull();
    }
}
