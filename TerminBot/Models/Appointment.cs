using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations.Schema;


namespace TerminBot.Models
{
    [Index(nameof(UserId), nameof(DayIso), nameof(TimeIso), IsUnique = true)]
    public class Appointment
    {
        public int Id { get; set; }
        public string? UserId { get; set; }

        public string? UserName { get; set; }
        public string? Day { get; set; }
        public string? Time { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ServiceType { get; set; }
        public string DayIso { get; set; }
        public string TimeIso { get; set; }
        public string? Contact { get; set; }

        public string BookingCode { get; set; } = default!;



    }
}
