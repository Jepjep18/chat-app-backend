using ChatAppBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatAppBackend.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Message> Messages { get; set; }
    }
}
