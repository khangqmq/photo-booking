using FluentValidation;

public class CreateBookingValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingValidator()
    {
        RuleFor(x => x.ServiceId).GreaterThan(0);
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.CustomerPhone).Matches("^(0|[+]84)[0-9]{9,10}$");
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.StartUtc).Must(d => d > DateTime.UtcNow)
            .WithMessage("Thời gian đặt phải ở tương lai");
    }
}