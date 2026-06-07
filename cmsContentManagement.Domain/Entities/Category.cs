using System.ComponentModel.DataAnnotations;

namespace cmsContentManagement.Domain.Entities;

public class Category
{
    [Key]
    public Guid CategoryId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public Guid OrganisationId { get; set; }
    
    [Required]
    public Guid UserId { get; set; }
    
    public ICollection<Content> Contents { get; set; } = new List<Content>();
}
