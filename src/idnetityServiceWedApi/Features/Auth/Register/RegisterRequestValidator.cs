using FluentValidation;

namespace idnetityServiceWedApi.Features.Auth.Register;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        // Mirrors options.Password in Program.cs so a request never passes validation
        // here only to be rejected later by UserManager.CreateAsync.
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.");
    }
}
