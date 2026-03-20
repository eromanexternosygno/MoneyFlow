using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MoneyFlow.Context;
using MoneyFlow.Entities;
using MoneyFlow.Interfaces;
using MoneyFlow.Models;

namespace MoneyFlow.Managers;

public class UserManager : IUserManager
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<UserManager> _logger;
    private readonly IPasswordHasher<User> _passwordHasher;


    // Constructor: Inyectamos el connection string y el logger
    public UserManager(AppDbContext dbContext, ILogger<UserManager> logger, IPasswordHasher<User> passwordHasher)
    {
        _dbContext = dbContext;
        _logger = logger;
        _passwordHasher = passwordHasher;
    }

    // Propiedad para la base de datos local (MoneyFlowDb)

    // Metodo: Login
    public async Task<UserViewModel> Login(LoginViewModel loginViewModel)
    {

        //Validate user existence and password
        var userEntity = await _dbContext.User
            .Where(u => u.Email == loginViewModel.Email && u.Password == loginViewModel.Password)
            .FirstOrDefaultAsync();

        if (userEntity == null)
        {
            _logger.LogWarning("Login failed for email: {Email}. User not found or incorrect password.", loginViewModel.Email);
            return null; // Return null if user not found or password is incorrect
        }

        try
        {
            // Map User Entity to UserViewModel
            var userViewModel = new UserViewModel
            {
                UserId = userEntity.UserId,
                Name = userEntity.FullName,
                Email = userEntity.Email
            };
            _logger.LogInformation("User logged in successfully: {Email}", loginViewModel.Email);
            return userViewModel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while mapping user data for email: {Email}", loginViewModel.Email);
            // throw error
            throw new Exception("An error occurred while processing the login request. Please try again later.");
        }
    }
    public async Task<UserViewModel> GetByEmail (string email)
    {
        var userEntity = await _dbContext.User
            .Where(u => u.Email == email)
            .FirstOrDefaultAsync();
        if (userEntity == null)
        {
            _logger.LogWarning("GetByEmail failed for email: {Email}. User not found.", email);
            return null; // Return null if user not found
        }
        try
        {
            // Map User Entity to UserViewModel
            var userViewModel = new UserViewModel
            {
                UserId = userEntity.UserId,
                Name = userEntity.FullName,
                Email = userEntity.Email
            };

            _logger.LogInformation("User retrieved successfully by email: {Email}", email);
            return userViewModel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while mapping user data for email: {Email}", email);
            // throw error
            throw new Exception("An error occurred while processing the request. Please try again later.");
        }
    }

    public async Task<LoginViewModel> LoginShow()
    {
        // Return empty LoginViewModel
        return await Task.FromResult(new LoginViewModel());
    }

    public async Task<UserViewModel?> ValidatePassword(string email,string password)
    {
        var user = await _dbContext.User.FirstOrDefaultAsync(u => u.Email == email);

        if ( user == null)
        {
            _logger.LogWarning("ValidatePassword failed for email: {Email}. User not found.", email);
            return null; // Return null if user not found
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

       if(result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("ValidatePassword failed for email: {Email}. Incorrect password.", email);
            return null; // Return null if password is incorrect
        }

        // Map User Entity to UserViewModel
        var userViewModel = new UserViewModel
        {
            UserId = user.UserId,
            Name = user.FullName,
            Email = user.Email
        };
        return userViewModel;
    }
}
