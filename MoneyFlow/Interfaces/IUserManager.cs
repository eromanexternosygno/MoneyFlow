using MoneyFlow.Models;

namespace MoneyFlow.Interfaces
{
    public interface IUserManager
    {
        // Login user
        Task<UserViewModel> Login(LoginViewModel loginViewModel);

        // Login ViewModel
        Task<LoginViewModel> LoginShow();

        //Implement GetByEmail
        Task<UserViewModel> GetByEmail(string email);

        Task<UserViewModel> ValidatePassword(string email,string password);
    }
}
