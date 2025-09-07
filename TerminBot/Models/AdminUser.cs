using System.ComponentModel.DataAnnotations;

namespace TerminBot.Models
{
    public class AdminUser
    {
        public int Id { get; set; }

        [Required, MaxLength(64)]
        public string Username { get; set; } = default!;


        [Required, MaxLength(512)]
        public string PasswordHash { get; set; } = default!;
    }
}
