namespace Wdc.Domain;

/// <summary>A criteria that carries what to fetch.</summary>
public interface IProjectionCarrier<out TEntity>
    where TEntity : class
{
    TEntity? Projection { get; }
}
