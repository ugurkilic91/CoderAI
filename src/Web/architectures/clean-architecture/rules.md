This solution uses the Clean Architecture template (Jason Taylor, `ca-sln`). The root namespace is the solution name: see the
`RootNamespace` values in the .csproj files (e.g. `HobbyTracker.Application`). Layers and allowed references:
Domain ← Application ← Infrastructure ← Web. Never reference an outer layer from an inner one.

## How to add a feature (example: "Hobbies" with entity "Hobby")
Create or change exactly these files, mirroring the reference files below:

1. **Entity** `src/Domain/Entities/Hobby.cs`: `public class Hobby : BaseAuditableEntity` (it already has `Id` and audit fields).
   Plain properties only (`public string? Name { get; set; }`), no EF attributes. Namespace `<Root>.Domain.Entities`.
2. **Register the DbSet in BOTH files** (output the COMPLETE updated files, keep the existing members):
   - `src/Application/Common/Interfaces/IApplicationDbContext.cs`: `DbSet<Hobby> Hobbies { get; }`
   - `src/Infrastructure/Data/ApplicationDbContext.cs`: `public DbSet<Hobby> Hobbies => Set<Hobby>();`
3. **EF configuration** `src/Infrastructure/Data/Configurations/HobbyConfiguration.cs` implementing `IEntityTypeConfiguration<Hobby>`
   (required fields, max lengths). The database is created with EnsureCreated: there are NO migrations to add.
4. **Use cases** in `src/Application/Hobbies/`:
   - Command: `Commands/CreateHobby/CreateHobby.cs` contains BOTH `public record CreateHobbyCommand : IRequest<int>` (init properties)
     and `public class CreateHobbyCommandHandler : IRequestHandler<CreateHobbyCommand, int>` (constructor-injected `IApplicationDbContext`).
   - Validator: `Commands/CreateHobby/CreateHobbyCommandValidator.cs` (`AbstractValidator<CreateHobbyCommand>`).
   - Update/Delete: load with `FindAsync([request.Id], cancellationToken)` then `Guard.Against.NotFound(request.Id, entity);`.
     A delete command may be positional: `public record DeleteHobbyCommand(int Id) : IRequest;`.
   - Query: `Queries/GetHobbies/GetHobbies.cs` with the query, its handler (`IApplicationDbContext` + `IMapper`) and the DTO class
     with a nested `private class Mapping : Profile`; read with `.AsNoTracking()...ProjectTo<HobbyDto>(_mapper.ConfigurationProvider)`.
     Every DTO property must exist on the entity with the same name: an existing test asserts that the AutoMapper configuration is valid.
   - Application already has global usings for Ardalis.GuardClauses, AutoMapper, AutoMapper.QueryableExtensions,
     Microsoft.EntityFrameworkCore, FluentValidation and MediatR. Add `using <Root>.Application.Common.Interfaces;` and
     `using <Root>.Domain.Entities;` where needed.
5. **Endpoints** `src/Web/Endpoints/Hobbies.cs`: `public class Hobbies : IEndpointGroup` with
   `public static void Map(RouteGroupBuilder groupBuilder)` registering NAMED static handler methods:
   `groupBuilder.MapGet(GetHobbies);`, `groupBuilder.MapPost(CreateHobby);`, `groupBuilder.MapPut(UpdateHobby, "{id}");`,
   `groupBuilder.MapDelete(DeleteHobby, "{id}");`. Handlers take `ISender sender` and return `TypedResults`.
   Do NOT call `RequireAuthorization()` unless the request asks for authorization.
6. **Unit tests** (see Testing).

Nothing has to be registered by hand: MediatR handlers, validators, AutoMapper profiles and endpoint groups are discovered
automatically. Do not change Program.cs or any DependencyInjection.cs.

## Testing
- Test projects: `tests/Application.UnitTests` (handlers, validators) and `tests/Domain.UnitTests` (pure domain logic).
- File path: `tests/Application.UnitTests/<Feature>/Commands/<UseCase>CommandTests.cs` (or `Queries/...`);
  namespace `<Root>.Application.UnitTests.<Feature>.Commands`.
- NUnit (`[Test]`, `[SetUp]`, `[TearDown]`) with Shouldly assertions (`value.ShouldBe(expected)`).
- Test handlers against a real `ApplicationDbContext` on in-memory SQLite, exactly like the reference test
  (no mocks of the DbContext). Test validators by calling `Validate` directly.
- Queries that need `IMapper`: build one with
  `new MapperConfiguration(cfg => cfg.AddMaps(typeof(<Root>.Application.Common.Interfaces.IApplicationDbContext).Assembly), NullLoggerFactory.Instance).CreateMapper()`
  (`using AutoMapper;` and `using Microsoft.Extensions.Logging.Abstractions;`).

## Conventions
- File-scoped namespaces matching the folder; one public type per file, except a command/query with its handler (and a query's DTO).
- Nullable reference types are enabled and warnings are errors: the code must compile without any warning.
- Async all the way, pass the `CancellationToken`.
- Write short XML documentation comments (`///`) in TURKISH for every public type and method you create
  (e.g. `/// <summary>Yeni bir hobi kaydı oluşturur.</summary>`). Identifiers stay in English.
