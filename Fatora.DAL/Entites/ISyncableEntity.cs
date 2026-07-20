namespace Fatora.DAL.Entites;

public interface ISyncableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
