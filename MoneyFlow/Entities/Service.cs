using System.ComponentModel.DataAnnotations;

namespace MoneyFlow.Entities;

public class Service
{
    public int ServiceId { get; set; }
    //Add Validation
    [Required]

    public int UserId { get; set; }
    [Required]
    public string Name { get; set; }
    [Required]
    public string Type { get; set; }

    // Foreing key relationship
    public User User { get; set; }
}
