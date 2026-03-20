using Microsoft.AspNetCore.Identity;

namespace MoneyFlow.Utilities
{
    public class PasswordService
    {
        private readonly PasswordHasher<object> _passwordHasher = new();
        
        public string Hash(string password)
        {
            return _passwordHasher.HashPassword(null, password); // Hash the password using the PasswordHasher
        }

        public bool Verify(string hashedPassword, string providedPassword)
        {
            var result = _passwordHasher.VerifyHashedPassword(null, hashedPassword, providedPassword); // Verify the provided password against the hashed password
            return result == PasswordVerificationResult.Success; // Verify the provided password against the hashed password
        }
    }
}
