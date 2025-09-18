namespace DotNetAtlas.Application.Common.Cache;

public interface ICacheableItem
{
    string CacheKey { get; }
}
