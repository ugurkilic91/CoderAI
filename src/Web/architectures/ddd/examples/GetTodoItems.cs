using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;

namespace CleanArchitecture.Application.TodoItems.Queries.GetTodoItems;

/// <summary>Bir listedeki yapılacak işleri başlığa göre sıralı döndürür.</summary>
public record GetTodoItemsQuery : IRequest<IReadOnlyCollection<TodoItemBriefDto>>
{
    public int ListId { get; init; }
}

public class GetTodoItemsQueryHandler : IRequestHandler<GetTodoItemsQuery, IReadOnlyCollection<TodoItemBriefDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetTodoItemsQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    /// <summary>Listedeki kayıtları veritabanında DTO'ya çevirerek (ProjectTo) okur.</summary>
    public async Task<IReadOnlyCollection<TodoItemBriefDto>> Handle(GetTodoItemsQuery request, CancellationToken cancellationToken)
    {
        return await _context.TodoItems
            .AsNoTracking()
            .Where(x => x.ListId == request.ListId)
            .OrderBy(x => x.Title)
            .ProjectTo<TodoItemBriefDto>(_mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Listeleme ekranı için yapılacak iş özeti.</summary>
public class TodoItemBriefDto
{
    public int Id { get; init; }

    public int ListId { get; init; }

    public string? Title { get; init; }

    public bool Done { get; init; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<TodoItem, TodoItemBriefDto>();
        }
    }
}
