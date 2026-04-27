using System.ComponentModel.DataAnnotations;

namespace APBD_TASK6.DTOs;

public class UpdateAppointmentRequestDto
{
    [Required]
    public int IdPatient { get; set; }
 
    [Required]
    public int IdDoctor { get; set; }
 
    [Required]
    public DateTime AppointmentDate { get; set; }
 
    [Required]
    public string Status { get; set; } = string.Empty;
 
    [Required]
    [StringLength(250, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
 
    [StringLength(500)]
    public string? InternalNotes { get; set; }
}