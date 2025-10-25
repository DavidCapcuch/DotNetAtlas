namespace DotNetAtlas.Domain.Common;

public abstract class Entity<TId> : IComparable, IComparable<Entity<TId>>
    where TId : IComparable<TId>
{
    public virtual TId Id { get; protected set; } = default!;

    public virtual byte[]? Timestamp { get; set; }

    protected Entity()
    {
    }

    protected Entity(TId id)
    {
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (IsTransient() || other.IsTransient())
        {
            return false;
        }

        return Id.Equals(other.Id);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public virtual int CompareTo(Entity<TId>? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        return Id.CompareTo(other.Id);
    }

    public virtual int CompareTo(object? other)
    {
        return CompareTo(other as Entity<TId>);
    }

    private bool IsTransient()
    {
        return Id.Equals(default(TId));
    }
}
