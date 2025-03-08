using UserManagementAPI.DbContexts;
using UserManagementAPI.Models;

namespace UserManagementAPI.DbSeeders
{
    public class DbSeeder
    {
        public void Seed(AppDbContext db)
        {
            if (!db.Users.Any())
            {
                db.Users.AddRange(Enumerable.Range(1, 20).Select(i => new User
                {
                    Name = $"User{i}",
                    Email = $"user{i}@example.com"
                }));
                db.SaveChanges();
            }
        }
    }
}
