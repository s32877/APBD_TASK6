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
        private static readonly HashSet<string> ValidStatuses =
            new(StringComparer.OrdinalIgnoreCase) { "Scheduled", "Completed", "Cancelled" };

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
            if (request.AppointmentDate <= DateTime.UtcNow)
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

        [HttpPut("{idAppointment:int}")]
        public async Task<IActionResult> UpdateAppointment(
            int idAppointment,
            [FromBody] UpdateAppointmentRequestDto request)
        {
            if (!ValidStatuses.Contains(request.Status))
                return BadRequest(new ErrorResponseDto("Status must be one of: Scheduled, Completed, Cancelled."));
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            string? currentStatus;
            DateTime currentDate;
            
            await using (var selectCmd = new SqlCommand(
                             "SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;",
                             connection))
            {
                selectCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
                await using var reader = await selectCmd.ExecuteReaderAsync();
 
                if (!await reader.ReadAsync())
                    return NotFound(new ErrorResponseDto($"Appointment {idAppointment} not found."));
 
                currentStatus = reader.GetString(0);
                currentDate = reader.GetDateTime(1);
            }
 
            bool dateChanging = request.AppointmentDate != currentDate;
            if (currentStatus == "Completed" && dateChanging)
                return Conflict(new ErrorResponseDto("Cannot change the date of a Completed appointment."));
 
            if (!await IsActiveAsync(connection, "Patients", "IdPatient", request.IdPatient))
                return BadRequest(new ErrorResponseDto($"Patient {request.IdPatient} does not exist or is not active."));
            if (!await IsActiveAsync(connection, "Doctors", "IdDoctor", request.IdDoctor))
                return BadRequest(new ErrorResponseDto($"Doctor {request.IdDoctor} does not exist or is not active."));
            if (dateChanging &&
                await DoctorHasConflictAsync(connection, request.IdDoctor, request.AppointmentDate, excludeId: idAppointment))
            {
                return Conflict(new ErrorResponseDto("The doctor already has a Scheduled appointment at that exact time."));
            }
            
            const string updateSql = """
                                     UPDATE dbo.Appointments
                                     SET IdPatient       = @IdPatient,
                                         IdDoctor        = @IdDoctor,
                                         AppointmentDate = @AppointmentDate,
                                         Status          = @Status,
                                         Reason          = @Reason,
                                         InternalNotes   = @InternalNotes
                                     WHERE IdAppointment = @IdAppointment;
                                     """;
 
            await using var updateCmd = new SqlCommand(updateSql, connection);
            updateCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            updateCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            updateCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            updateCmd.Parameters.AddWithValue("@Status", request.Status);
            updateCmd.Parameters.AddWithValue("@Reason", request.Reason);
            updateCmd.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
 
            await updateCmd.ExecuteNonQueryAsync();
            return Ok();
        }

        [HttpDelete("{idAppointment:int}")]
        public async Task<IActionResult> DeleteAppointment(int idAppointment)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
 
            string? currentStatus;
            await using (var selectCmd = new SqlCommand(
                             "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;",
                             connection))
            {
                selectCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
                await using var reader = await selectCmd.ExecuteReaderAsync();
 
                if (!await reader.ReadAsync())
                    return NotFound(new ErrorResponseDto($"Appointment {idAppointment} not found."));
 
                currentStatus = reader.GetString(0);
            }
 
            if (currentStatus == "Completed")
                return Conflict(new ErrorResponseDto("Cannot delete a Completed appointment."));
 
            await using var deleteCmd = new SqlCommand(
                "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;",
                connection);
            deleteCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
            await deleteCmd.ExecuteNonQueryAsync();
 
            return NoContent();
        }
        private static async Task<bool> IsActiveAsync(
            SqlConnection connection, string tableName, string idColumn, int id)
        {
            await using var cmd = new SqlCommand(
                $"SELECT COUNT(1) FROM dbo.{tableName} WHERE {idColumn} = @Id AND IsActive = 1;",
                connection);
            cmd.Parameters.AddWithValue("@Id", id);
            return (int)(await cmd.ExecuteScalarAsync())! > 0;
        }
        private static async Task<bool> DoctorHasConflictAsync(
            SqlConnection connection, int idDoctor, DateTime appointmentDate, int? excludeId)
        {
            var sql = """
                      SELECT COUNT(1) FROM dbo.Appointments
                      WHERE IdDoctor        = @IdDoctor
                        AND AppointmentDate = @AppointmentDate
                        AND Status          = N'Scheduled'
                      """;
 
            if (excludeId.HasValue)
                sql += " AND IdAppointment <> @ExcludeId";
 
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@IdDoctor",        idDoctor);
            cmd.Parameters.AddWithValue("@AppointmentDate", appointmentDate);
            if (excludeId.HasValue)
                cmd.Parameters.AddWithValue("@ExcludeId", excludeId.Value);
 
            return (int)(await cmd.ExecuteScalarAsync())! > 0;
        }
    }
    
}


