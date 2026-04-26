using Przychodnia.DTOs;

namespace Przychodnia.Services;

public interface IAppointmentService
{
    Task<IEnumerable<AppointmentListDto>> GetAllAppointments(string? status, string? patientLastName);
    Task<AppointmentDetailsDto?> GetAppointmentDetails(int idAppointment);
    Task<int> CreateAppointment(CreateAppointmentRequestDto request);
    Task<int> UpdateAppointment(int idAppointment,  UpdateAppointmentRequestDto request);
    Task<int> DeleteAppointment(int idAppointment);
}