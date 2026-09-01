using Api.Mappers;
using Application.Commands.PostAdjustingEntry;
using Application.Commands.ReconcileLedger;
using Application.Queries.GetOrderMoneyTrail;
using Application.Queries.GetTrialBalance;
using Domain.Enums;
using Grpc.Core;
using Infrastructure.Options;
using MediatR;
using Microsoft.Extensions.Options;
using Protos.AdminOps;
using CommandLeg = Application.Commands.PostAdjustingEntry.AdjustingEntryLeg;

namespace Api.GrpcServices;

public sealed class AdminAccountingGrpcService(
    IMediator mediator,
    IOptions<ReportingOptions> reportingOptions,
    ILogger<AdminAccountingGrpcService> logger)
    : AdminAccountingService.AdminAccountingServiceBase
{
    public override async Task<GetTrialBalanceResponse> GetTrialBalance(
        GetTrialBalanceRequest request,
        ServerCallContext context)
    {
        var currency = string.IsNullOrWhiteSpace(request.ReportingCurrency)
            ? reportingOptions.Value.ReportingCurrency
            : request.ReportingCurrency;

        var result = await mediator.Send(new GetTrialBalanceQuery(currency), context.CancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            logger.LogWarning("GetTrialBalance failed. Currency={Currency}, Error={Error}", currency, result.Error);
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, result.Error ?? "GetTrialBalance failed."));
        }

        var balance = result.Value;
        var response = new GetTrialBalanceResponse
        {
            ReportingCurrency = balance.ReportingCurrency,
            ReportingDebits = balance.ReportingDebits.ToDecimalValue(),
            ReportingCredits = balance.ReportingCredits.ToDecimalValue(),
            IsBalanced = balance.IsBalanced,
        };

        response.TransactionCurrencyBalances.AddRange(balance.TransactionCurrencyBalances.Select(Map));
        response.ReportingCurrencyBalances.AddRange(balance.ReportingCurrencyBalances.Select(Map));

        return response;
    }

    public override async Task<GetOrderMoneyTrailResponse> GetOrderMoneyTrail(
        GetOrderMoneyTrailRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid OrderId is required."));

        var result = await mediator.Send(new GetOrderMoneyTrailQuery(orderId), context.CancellationToken);

        if (!result.IsSuccess || result.Value is null)
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, result.Error ?? "GetOrderMoneyTrail failed."));

        var response = new GetOrderMoneyTrailResponse();

        response.Transactions.AddRange(result.Value.Select(t =>
        {
            var transaction = new MoneyTrailTransaction
            {
                TransactionId = t.TransactionId.ToString(),
                TransactionRef = t.TransactionRef,
                RefType = t.RefType.ToString(),
                RefId = t.RefId,
                Currency = t.Currency,
                OccurredAt = t.OccurredAt.ToString("O"),
            };

            transaction.Entries.AddRange(t.Entries.Select(e => new MoneyTrailEntry
            {
                Account = e.Account.ToString(),
                Direction = e.Direction.ToString(),
                Amount = e.Amount.ToDecimalValue(),
                Currency = e.Currency,
            }));

            return transaction;
        }));

        return response;
    }

    public override async Task<GetLedgerHealthResponse> GetLedgerHealth(
        GetLedgerHealthRequest request,
        ServerCallContext context)
    {
        // ReportIncidents: false — an operator refreshing a page must not page the finance channel.
        var result = await mediator.Send(
            new ReconcileLedgerCommand(
                request.MaxFindings > 0 ? request.MaxFindings : 20,
                request.MaxAcceptableLag,
                ReportIncidents: false),
            context.CancellationToken);

        if (!result.IsSuccess || result.Value is null)
            throw new RpcException(new Status(
                StatusCode.Internal, result.Error ?? "GetLedgerHealth failed."));

        var response = new GetLedgerHealthResponse
        {
            IsHealthy = result.Value.IsHealthy,
            CurrenciesChecked = result.Value.CurrenciesChecked,
        };

        response.Findings.AddRange(result.Value.Findings);
        return response;
    }

    public override async Task<PostAdjustingEntryResponse> PostAdjustingEntry(
        PostAdjustingEntryRequest request,
        ServerCallContext context)
    {
        Guid? orderId = Guid.TryParse(request.OrderId, out var parsed) ? parsed : null;

        var legs = new List<CommandLeg>();

        foreach (var leg in request.Legs)
        {
            if (!Enum.TryParse<LedgerAccount>(leg.Account, ignoreCase: true, out var account))
                return Failure($"Unknown ledger account '{leg.Account}'.");

            if (!Enum.TryParse<EntryDirection>(leg.Direction, ignoreCase: true, out var direction))
                return Failure($"Unknown entry direction '{leg.Direction}'. Use Debit or Credit.");

            legs.Add(new CommandLeg(account, direction, leg.Amount?.ToDecimal() ?? 0m));
        }

        var result = await mediator.Send(
            new PostAdjustingEntryCommand(
                request.AdjustmentId,
                request.Currency,
                legs,
                request.Reason,
                request.PostedBy,
                orderId),
            context.CancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            logger.LogWarning(
                "PostAdjustingEntry failed. AdjustmentId={AdjustmentId}, Error={Error}",
                request.AdjustmentId,
                result.Error);

            return Failure(result.Error ?? "PostAdjustingEntry failed.");
        }

        return new PostAdjustingEntryResponse { Success = true, TransactionId = result.Value };
    }

    private static PostAdjustingEntryResponse Failure(string message) =>
        new() { Success = false, ErrorMessage = message };

    private static AccountBalance Map(Application.Interfaces.AccountBalance balance) =>
        new()
        {
            Account = balance.Account.ToString(),
            Currency = balance.Currency,
            Debits = balance.Debits.ToDecimalValue(),
            Credits = balance.Credits.ToDecimalValue(),
            Balance = balance.Balance.ToDecimalValue(),
        };
}
