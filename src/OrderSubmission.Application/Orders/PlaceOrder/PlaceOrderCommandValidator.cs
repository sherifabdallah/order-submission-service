using FluentValidation;
using OrderSubmission.Application.Idempotency;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Application.Orders.PlaceOrder;

internal sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .WithMessage("The Idempotency-Key header is required.")
            .MaximumLength(IdempotencyRecord.KeyMaxLength)
            .WithMessage($"The Idempotency-Key header must be at most {IdempotencyRecord.KeyMaxLength} characters.")
            .Must(BeVisibleAscii)
            .WithMessage("The Idempotency-Key header may only contain visible ASCII characters.");

        RuleFor(command => command.CustomerReference)
            .NotEmpty()
            .MaximumLength(Order.CustomerReferenceMaxLength);

        RuleFor(command => command.Items)
            .NotEmpty()
            .WithMessage("An order must contain at least one line item.")
            .Must(items => items.Count <= Order.MaxLines)
            .WithMessage($"An order can contain at most {Order.MaxLines} line items.");

        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(line => line.ProductCode)
                .NotEmpty()
                .MaximumLength(OrderLine.ProductCodeMaxLength);

            item.RuleFor(line => line.Quantity)
                .GreaterThan(0)
                .LessThanOrEqualTo(OrderLine.MaxQuantity);

            item.RuleFor(line => line.UnitPrice)
                .GreaterThan(0)
                .LessThanOrEqualTo(OrderLine.MaxUnitPrice)
                .PrecisionScale(18, OrderLine.UnitPriceDecimals, ignoreTrailingZeros: true)
                .WithMessage($"Unit price must have at most {OrderLine.UnitPriceDecimals} decimal places.");
        });
    }

    private static bool BeVisibleAscii(string? key) =>
        key is null || key.All(character => character is >= '!' and <= '~');
}
