using FluentResults;

namespace DotNetAtlas.Application.Common.CQS
{
    public interface IQueryHandler<in TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct);
    }
}