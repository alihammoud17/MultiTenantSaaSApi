using System;

namespace Domain.DTOs;

public sealed record BillingInvoiceListItemResponse(
    Guid InvoiceId,
    string ExternalInvoiceId,
    Guid SubscriptionId,
    decimal AmountDue,
    decimal AmountPaid,
    string Currency,
    string Status,
    DateTime IssuedAtUtc,
    DateTime? DueAtUtc,
    DateTime? PaidAtUtc);
