using System.ComponentModel.DataAnnotations;

namespace KafkaPerformance.Shared.Entities;

public class ApiRequest
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string MaCSKCB { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public ApiRequest()
    {
        Id = Guid.NewGuid();
        MaCSKCB = string.Empty;
        Status = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public ApiRequest(Guid id, string maCSKCB, string status)
    {
        Id = id;
        MaCSKCB = maCSKCB;
        Status = status;
        CreatedAt = DateTime.UtcNow;
    }
}
