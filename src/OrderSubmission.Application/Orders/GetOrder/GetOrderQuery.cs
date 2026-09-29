using OrderSubmission.Application.Abstractions.Messaging;
using OrderSubmission.Application.Common.Results;

namespace OrderSubmission.Application.Orders.GetOrder;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDetails>;

internal sealed class GetOrderQueryHandler(IOrderReadService readService) : IQueryHandler<GetOrderQuery, OrderDetails>
{
    public async Task<Result<OrderDetails>> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await readService.GetOrderAsync(query.OrderId, cancellationToken);
        return order is null ? OrderErrors.NotFound(query.OrderId) : order;
    }
}
