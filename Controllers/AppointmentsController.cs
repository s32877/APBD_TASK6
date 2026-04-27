using System.Data;
using Microsoft.AspNetCore.Mvc;
using APBD_TASK6.DTOs;
using Microsoft.Data.SqlClient;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppointmentsController : ControllerBase
    {
        private readonly string _connectionString;

        public AppointmentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException(
                                    "Missing 'DefaultConnection' in appsettings.json.");
        }

        [HttpGet]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] string? status,
            [FromQuery] string? patientLastName)
        {
            const string sql = """
                               SELECT
                                   a.IdAppointment,
                                   a.AppointmentDate,
                                   a.Status,
                                   a.Reason,
                                   p.FirstName + N' ' + p.LastName AS PatientFullName,
                                   p.Email AS PatientEmail
                               FROM dbo.Appointments a
                               JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                               WHERE (@Status IS NULL OR a.Status = @Status)
                                 AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                               ORDER BY a.AppointmentDate;
                               """;
            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);

            await connection.OpenAsync();
            var results = new List<AppointmentListDto>();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new AppointmentListDto
                {
                    IdAppointment = reader.GetInt32(0),
                    AppointmentDate = reader.GetDateTime(1),
                    Status = reader.GetString(2),
                    Reason = reader.GetString(3),
                    PatientFullName = reader.GetString(4),
                    PatientEmail = reader.GetString(5),
                });
            }

            return Ok(results);
        }

        [HttpGet("{idAppointment:int}")]
        public async Task<IActionResult> GetAppointment(int idAppointment)
        {
            const string sql = """
                SELECT
                    a.IdAppointment,
                    a.AppointmentDate,
                    a.Status,
                    a.Reason,
                    a.InternalNotes,
                    a.CreatedAt,
                    p.FirstName + N' ' + p.LastName AS PatientFullName,
                    p.Email AS PatientEmail,
                    p.PhoneNumber AS PatientPhone,
                    d.FirstName + N' ' + d.LastName AS DoctorFullName,
                    d.LicenseNumber,
                    s.Name AS Specialization
                FROM dbo.Appointments a
                JOIN dbo.Patients        p ON p.IdPatient        = a.IdPatient
                JOIN dbo.Doctors         d ON d.IdDoctor         = a.IdDoctor
                JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
                WHERE a.IdAppointment = @IdAppointment;
                """;
 
            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@IdAppointment", idAppointment);
 
            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
 
            if (!await reader.ReadAsync())
                return NotFound(new ErrorResponseDto($"Appointment {idAppointment} not found."));
 
            var dto = new AppointmentDetailsDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                                          ? null
                                          : reader.GetString(reader.GetOrdinal("InternalNotes")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
                PatientPhone = reader.IsDBNull(reader.GetOrdinal("PatientPhone"))
                                          ? string.Empty
                                          : reader.GetString(reader.GetOrdinal("PatientPhone")),
                DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
                DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("LicenseNumber")),
                Specialization = reader.GetString(reader.GetOrdinal("Specialization")),
            };
 
            return Ok(dto);
        }
 

        [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
        {
            if (request.AppointmentDate < DateTime.UtcNow)
            {
                return BadRequest(new ErrorResponseDto("Appointment date cannot be in the past."));
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            int newId;
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                const string insertSql = """
                                         INSERT INTO dbo.Appointments (IdDoctor, IdPatient, AppointmentDate, Reason, Status)
                                         OUTPUT INSERTED.IdAppointment
                                         VALUES (@IdDoctor, @IdPatient, @AppointmentDate, @Reason, 'Scheduled');
                                         """;

                await using var command = new SqlCommand(insertSql, connection, transaction);
                command.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                command.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                command.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                command.Parameters.AddWithValue("@Reason", request.Reason);

                newId = (int)(await command.ExecuteScalarAsync())!;
                await transaction.CommitAsync();
            }

            return CreatedAtRoute(nameof(GetAppointments), new { id = newId }, null);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateAppointment([FromBody] UpdateAppointmentRequestDto request)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            
            return null;
        }
    }
}
