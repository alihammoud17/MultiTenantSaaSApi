using Domain.DTOs;
using Domain.Outputs;

namespace Domain.Interfaces;

public interface IBillingCallbackProcessor
{
    Task<BillingCallbackProcessingResult> ProcessAsync(BillingCallbackRequest request, CancellationToken cancellationToken = default);
}
