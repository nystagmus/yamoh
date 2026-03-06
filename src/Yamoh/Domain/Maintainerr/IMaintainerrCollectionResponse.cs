namespace Yamoh.Domain.Maintainerr;

public interface IMaintainerrCollectionResponse
{
    public int Id { get; }
    public string? MediaServerId { get; }
    public string? MediaServerType { get; }
    public string? LibraryId { get; }
    public string? Title { get; }
    public string? Description { get; }
    public bool IsActive { get; }
    public int? DeleteAfterDays { get; }
    public MaintainerrDataType Type { get; }
    public List<IMaintainerrMediaResponse>? Media { get; }
}