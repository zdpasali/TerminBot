using System;
using System.ComponentModel.DataAnnotations;

namespace TerminBot.Models
{
    public class ServiceRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ProblemType { get; set; }

        [Required]
        public string Address { get; set; }

        [Required]
        public DateTime RequestedDateTime { get; set; }

        [Required]
        public string Contact { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
