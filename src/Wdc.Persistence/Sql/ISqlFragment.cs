namespace Wdc.Persistence;

/// <summary>Something that renders itself into a statement.</summary>
public interface ISqlFragment
{
    void Render(SqlWriter writer);
}
