using Microsoft.AspNetCore.Mvc;
using Przychodnia.DTOs;
using Przychodnia.Services;

namespace Przychodnia.Controllers;
[Route("api/[controller]")]
[ApiController]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;
    public AppointmentsController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var appointments = await _appointmentService.GetAllAppointments(status, patientLastName);
        return Ok(appointments);
    }

    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointment(int idAppointment)
    {
        try
        {
            var appointment = await _appointmentService.GetAppointmentDetails(idAppointment);
            return Ok(appointment);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponseDto{Message = ex.Message});
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        try
        {
            var newId = await _appointmentService.CreateAppointment(request);
            return Created($"/api/appointments/{newId}", new {IdAppointment = newId});
        } catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponseDto{Message = ex.Message});
        } catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto{Message = ex.Message});
        }
    }

    [HttpPut("{idAppointment}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment
        , [FromBody] UpdateAppointmentRequestDto request)
    {
        try
        {
            await _appointmentService.UpdateAppointment(idAppointment, request);
            return Ok();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponseDto{Message = ex.Message});
        } catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponseDto{Message = ex.Message});
        } catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto{Message = ex.Message});
        }
    }

    [HttpDelete("{idAppointment}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        try
        {
            await _appointmentService.DeleteAppointment(idAppointment);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponseDto{Message = ex.Message});
        } catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto{Message = ex.Message});
        }
    }
}