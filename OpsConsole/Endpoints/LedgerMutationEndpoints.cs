using System.Security.Claims;
using Grpc.Core;
using Protos.AdminOps;

namespace OpsConsole.Endpoints;

public static class LedgerMutationEndpoints
{
    public sealed record AdjustingEntryLegRequest(string Account, string Direction, decimal Amount);

    public sealed record PostAdjustmentRequest(
        string AdjustmentId,
        string Currency,
        List<AdjustingEntryLegRequest> Legs,
        string Reason,
        string? OrderId);

    public static void MapLedgerMutationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ledger")
            .WithTags("LedgerMutations")
            .RequireAuthorization("OpsAdmin")
            .RequireRateLimiting("ops-mutation");

        // The only ledger mutation an operator gets. It appends a balanced reversing/adjusting
        // transaction; there is deliberately no edit or delete endpoint.
        group.MapPost("/adjustments", async (
            PostAdjustmentRequest request,
            ClaimsPrincipal user,
            AdminAccountingService.AdminAccountingServiceClient client,
            ILoggerFactory loggerFactory) =>
        {
            var audit = loggerFactory.CreateLogger("OpsConsole.Audit");
            var operatorId = GetOperatorId(user);

            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required for a manual adjustment." });

            if (request.Legs is null || request.Legs.Count < 2)
                return Results.BadRequest(new { error = "A manual adjustment needs at least two legs." });

            audit.LogWarning(
                "AUDIT action=PostLedgerAdjustment adjustmentId={AdjustmentId} operator={Operator} reason={Reason} result=attempting",
                request.AdjustmentId, operatorId, request.Reason);

            var grpcRequest = new PostAdjustingEntryRequest
            {
                AdjustmentId = request.AdjustmentId ?? string.Empty,
                Currency = request.Currency ?? string.Empty,
                Reason = request.Reason,
                // Taken from the token, never from the request body: the operator cannot claim
                // someone else posted the entry.
                PostedBy = operatorId,
                OrderId = request.OrderId ?? string.Empty,
            };

            grpcRequest.Legs.AddRange(request.Legs.Select(l => new AdjustingEntryLeg
            {
                Account = l.Account,
                Direction = l.Direction,
                Amount = ToDecimalValue(l.Amount),
            }));

            try
            {
                var response = await client.PostAdjustingEntryAsync(grpcRequest);

                audit.LogWarning(
                    "AUDIT action=PostLedgerAdjustment adjustmentId={AdjustmentId} operator={Operator} success={Success} transactionId={TransactionId} message={Message}",
                    request.AdjustmentId, operatorId, response.Success, response.TransactionId, response.ErrorMessage);

                return response.Success
                    ? Results.Ok(new { transactionId = response.TransactionId })
                    : Results.Conflict(new { error = response.ErrorMessage });
            }
            catch (RpcException ex)
            {
                audit.LogWarning(
                    "AUDIT action=PostLedgerAdjustment adjustmentId={AdjustmentId} operator={Operator} success=false message={Message}",
                    request.AdjustmentId, operatorId, ex.Status.Detail);
                throw;
            }
        });
    }

    private static Protos.Common.DecimalValue ToDecimalValue(decimal value)
    {
        var units = decimal.Truncate(value);
        var nanos = (int)((value - units) * 1_000_000_000m);
        return new Protos.Common.DecimalValue { Units = (long)units, Nanos = nanos };
    }

    private static string GetOperatorId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";
}
