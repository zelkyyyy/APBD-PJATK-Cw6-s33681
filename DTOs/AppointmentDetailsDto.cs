namespace Przychodnia.DTOs;

public class AppointmentDetailsDto : AppointmentListDto
{
    public string PatientPhoneNumber { get; set; } = string.Empty;
    public string DoctorFullName { get; set; } = string.Empty;
    public string DoctorSpecialization { get; set; } = string.Empty;
    public string? InternalNotes { get; set; } = string.Empty;  
    public DateTime CreatedAt { get; set; }
}