using CleanArchitecture.Application.TodoItems.Commands.CreateTodoItem;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.TodoItems.Commands;

/// <summary>Handler'ı bellek içi SQLite üzerinde gerçek ApplicationDbContext ile, doğrulayıcıyı doğrudan test eder.</summary>
public class CreateTodoItemCommandTests
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        // Her test kendi boş veritabanıyla başlar; bağlantı açık kaldığı sürece bellek içi veritabanı yaşar.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task ShouldCreateTodoItem()
    {
        var list = new TodoList { Title = "Shopping" };
        _context.TodoLists.Add(list);
        await _context.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateTodoItemCommandHandler(_context);
        var id = await handler.Handle(new CreateTodoItemCommand { ListId = list.Id, Title = "Tasks" }, CancellationToken.None);

        var item = await _context.TodoItems.FindAsync(id);
        item.ShouldNotBeNull();
        item.Title.ShouldBe("Tasks");
    }

    [Test]
    public void ShouldRequireTitle()
    {
        var result = new CreateTodoItemCommandValidator().Validate(new CreateTodoItemCommand { ListId = 1, Title = "" });

        result.IsValid.ShouldBeFalse();
    }
}
