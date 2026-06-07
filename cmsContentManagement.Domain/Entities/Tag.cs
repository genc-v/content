using System.ComponentModel.DataAnnotations;

namespace cmsContentManagement.Domain.Entities;

public class Tag
{
    [Key]
    public Guid TagId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid OrganisationId { get; set; }
    
    [Required]
    public Guid UserId { get; set; }
    
    public ICollection<Content> Contents { get; set; } = new List<Content>();
}
