using FluentValidation;

namespace BusinessCard.Application.BusinessCards;

internal sealed class BusinessCardParseRequestValidator : AbstractValidator<BusinessCardParseRequest>
{
    public BusinessCardParseRequestValidator()
    {
        RuleFor(x => x.Lines)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("At least one OCR line is required.")
            .Must(lines => lines!.Count <= ParseLimits.MaxLines)
            .WithMessage($"At most {ParseLimits.MaxLines} lines are allowed.")
            .Must(lines => lines!.Sum(l => l.Text?.Length ?? 0) <= ParseLimits.MaxTotalLength)
            .WithMessage($"Total text must not exceed {ParseLimits.MaxTotalLength} characters.")
            .Must(lines => lines!.Select(l => l.Order).Distinct().Count() == lines!.Count)
            .WithMessage("Line order values must be unique.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Text).NotEmpty().MaximumLength(ParseLimits.MaxLineLength);
            line.RuleFor(l => l.Order).InclusiveBetween(1, 1_000);
            line.RuleFor(l => l.Region).NotNull().IsInEnum();
            line.RuleFor(l => l.Size).NotNull().IsInEnum();
            line.RuleFor(l => l.Align).IsInEnum();
            line.RuleFor(l => l.Gap).IsInEnum();
        });
    }
}
