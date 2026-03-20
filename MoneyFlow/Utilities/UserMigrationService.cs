using MoneyFlow.Context;

namespace MoneyFlow.Utilities
{
    public class UserMigrationService
    {
        private readonly AppDbContext _dbContext;
        private readonly PasswordService _passwordService;

        public UserMigrationService(AppDbContext dbContext, PasswordService passwordService)
        {
            _dbContext = dbContext;
            _passwordService = passwordService;
        }

        public async Task MigrateUsersAsync()
        {
            var usersToMigrate = _dbContext.User.Where(u => u.Password != null).ToList();
            foreach (var user in usersToMigrate)
            {
                try
                {
                    // Hash the existing password
                    var hashedPassword = _passwordService.Hash(user.Password);
                    // Update the user's password with the hashed version
                    user.PasswordHash = hashedPassword;
                    // Save changes to the database
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Log the error and continue with the next user
                    Console.WriteLine($"Error migrating user {user.Email}: {ex.Message}");
                }
            }
        }

    }
}
