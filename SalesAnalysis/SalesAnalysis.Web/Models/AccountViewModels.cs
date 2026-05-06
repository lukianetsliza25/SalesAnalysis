namespace SalesAnalysis.Web.Models
{
    // Окрема модель для реєстрації
    public class RegisterViewModel
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // Окрема модель для входу
    public class LoginViewModel
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // Об'єднана модель для спільної сторінки (Auth.cshtml)
    public class AuthViewModel
    {
        public LoginViewModel Login { get; set; } = new LoginViewModel();
        public RegisterViewModel Register { get; set; } = new RegisterViewModel();
    }
}