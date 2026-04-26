using System.Data;
using Microsoft.Data.SqlClient;
using Przychodnia.DTOs;

namespace Przychodnia.Services;

public class AppointmentService : IAppointmentService
{
    private readonly string _connectionString;

    public AppointmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
                            ?? throw new ArgumentException("No connection string found");
        
    }

    public async Task<IEnumerable<AppointmentListDto>> GetAllAppointments(string? status, string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var query = """
                    SELECT 
                        a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, 
                        p.FirstName + ' ' + p.LastName AS PatientFullName, 
                        p.Email AS PatientEmail
                    FROM dbo.Appointments a
                    JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                    WHERE (@Status IS NULL OR a.Status = @Status)
                      AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                    ORDER BY a.AppointmentDate;
                    """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 30)
            { Value = (object?)status ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@PatientLastName", SqlDbType.NVarChar, 80){Value = (object?)patientLastName ?? DBNull.Value });

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto()
            {
                IdAppointment =  reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            });
        }
        return appointments;
    }

    public async Task<AppointmentDetailsDto> GetAppointmentById(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = """
                    SELECT 
                        a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, a.InternalNotes, a.CreatedAt,
                        p.FirstName + ' ' + p.LastName AS PatientFullName, p.Email AS PatientEmail, p.PhoneNumber AS PatientPhoneNumber,
                        d.FirstName + ' ' + d.LastName AS DoctorFullName,
                        s.Name AS DoctorSpecialization
                    FROM dbo.Appointments a
                    JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                    JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
                    JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
                    WHERE a.IdAppointment = @IdAppointment;
                    """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IDAppointment", idAppointment);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new KeyNotFoundException("Nie znaleziono wizyty");
        }

        return new AppointmentDetailsDto()
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment"))
            , AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate"))
            , Status = reader.GetString(reader.GetOrdinal("Status"))
            , Reason = reader.GetString(reader.GetOrdinal("Reason"))
            , InternalNotes = reader.GetString(reader.GetOrdinal("InternalNotes"))
            , CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            , PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName"))
            , PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            , PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber"))
            , DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName"))
            , DoctorSpecialization = reader.GetString(reader.GetOrdinal("DoctorSpecialization")),
        };
    }

    public async Task<int> CreateAppointment(CreateAppointmentRequestDto request)
    {
        if (request.AppointmentDate < DateTime.Now)
        {
            throw new ArgumentException("Termin wizyty nie może być w przeszłości");
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        if (!await isPatientActive(connection, request.IdPatient))
        {
            throw new ArgumentException("Pacjent musi być aktywny.");
        }

        if (!await isDoctorActive(connection, request.IdDoctor))
        {
            throw new ArgumentException("Doktor musi być aktywny.");
        }

        if (await DoctorHasConflict(connection, request.IdDoctor, request.AppointmentDate))
        {
            throw new InvalidOperationException("Lekarz ma już wizytę w tym terminie.");
        }

        var query = """
                    INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
                    VALUES (@IdPatient, @IdDoctor, @AppointmentDate, 'Scheduled',@Reason)
                    """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        command.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        command.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        command.Parameters.AddWithValue("@Reason", request.Reason);
        
        return (int)await command.ExecuteScalarAsync();
    }

    public async Task UpdateAppointment(int idAppointment, UpdateAppointmentRequestDto request)
    {
        var statuses = new[] {"Scheduled", "Completed", "Cancelled"};
        if (!statuses.Contains(request.Status))
        {
            throw new ArgumentException("Błędny status");
        }
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var currentAppointmentQuery = """
                                      SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @IdAppointment
                                      """;
        await using var command = new SqlCommand(currentAppointmentQuery, connection);
        command.Parameters.AddWithValue("@IdAppointment", idAppointment);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new KeyNotFoundException("Nie znaleziono wizyty");
        
        var currentStatus = reader.GetString(reader.GetOrdinal("Status"));
        var currentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate"));

        await reader.CloseAsync();

        if (currentStatus == "Completed" && request.AppointmentDate != currentDate)
        {
            throw new  ArgumentException("Nie można zmienić terminu zakończonej wizyty.");
        }

        if (!await isPatientActive(connection, request.IdPatient))
        {
            throw new ArgumentException("Pacjent nie jest aktywny.");
        }

        if (!await isDoctorActive(connection, request.IdDoctor))
        {
            throw new ArgumentException("Lekarz nie jest aktywny.");
        }

        if (request.AppointmentDate != currentDate &&
            await DoctorHasConflict(connection, request.IdDoctor, request.AppointmentDate, idAppointment))
        {
            throw new InvalidOperationException("Lekarz ma już zaplanowaną inną wizytę w tym terminie.");
        }

        var updateQuery = """
                            UPDATE dbo.Appointments set IdPatient = @IdPatient, IdDoctor = @IdDoctor, AppointmentDate = @AppointmentDate, Status = @Status, Reason = @Reason, InternalNotes = @InternalNotes 
                                                    where IdAppointment = @IdAppointment;
                          """;
        await using var updateCommand = new SqlCommand(updateQuery, connection);
        updateCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);
        updateCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        updateCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        updateCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        updateCommand.Parameters.AddWithValue("@Status", request.Status);
        updateCommand.Parameters.AddWithValue("@Reason", request.Reason);
        updateCommand.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
        
        await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task DeleteAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var statusQuery = """
                          SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment
                          """;
        var command = new SqlCommand(statusQuery, connection);
        command.Parameters.AddWithValue("@IdAppointment", idAppointment);
        
        var statusResult = await command.ExecuteScalarAsync();
        if (statusResult == null)
            throw new KeyNotFoundException("Nie znaleziono wizyty.");
        
        var status = statusResult.ToString();
        if (status == "Completed")
        {
            throw new InvalidOperationException("Nie można usunąć zakończonej wizyty.");
        }

        var deleteQuery = """
                            DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;
                          """;
        await using var deleteCommand = new SqlCommand(deleteQuery, connection);
        deleteCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    private async Task<bool> isPatientActive(SqlConnection connection, int patientId)
    {
        var query = """
                    SELECT IsActive From dbo.Patients WHERE IdPatient = @IdPatient;
                    """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdPatient", patientId);
        return (bool)await command.ExecuteScalarAsync();
    }
    private async Task<bool> isDoctorActive(SqlConnection connection, int doctorId)
    {
        var query = """
                    SELECT IsActive From dbo.Doctors WHERE IdDoctor = @IdDoctor;
                    """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdDoctor", doctorId);
        return (bool)await command.ExecuteScalarAsync();
    }

    private async Task<bool> DoctorHasConflict(SqlConnection connection, int doctorId, DateTime appointmentDate
        , int? excludeAppointmentId = null)
    {
        var query = """
                    Select count(1) from dbo.appointments where IdDoctor = @IdDoctor and appointmentDate = @AppointmentDate and (@ExcludeId is null or IdAppointment != @ExcludeId);
                    """;
        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@IdDoctor",  doctorId);
        cmd.Parameters.AddWithValue("@AppointmentDate", appointmentDate);
        cmd.Parameters.AddWithValue("@ExcludeId", excludeAppointmentId);
        var result =  (int)await cmd.ExecuteScalarAsync();
        return result > 0;
        
    }

}