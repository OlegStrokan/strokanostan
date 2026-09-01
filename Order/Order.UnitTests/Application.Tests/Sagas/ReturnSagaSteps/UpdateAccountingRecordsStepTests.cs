using Application.Sagas.Steps;
using Application.DTOs;
using Application.Gateways;
using Application.Sagas.ReturnSaga;
using Application.Sagas.ReturnSaga.Steps;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.Tests.Sagas.ReturnSagaSteps;

public class UpdateAccountingRecordsStepTests
{
    private readonly IAccountingGateway _accountingGateway = Substitute.For<IAccountingGateway>();
    private readonly ILogger<UpdateAccountingRecordsStep> _logger =
        Substitute.For<ILogger<UpdateAccountingRecordsStep>>();
    private readonly UpdateAccountingRecordsStep _step;

    public UpdateAccountingRecordsStepTests()
    {
        _step = new UpdateAccountingRecordsStep(_accountingGateway, _logger);
    }

    #region ExecuteAsync tests

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccess_WhenRevenueReversalSucceeds()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RefundId = "REF-123 " };
        var expectedReversalId = "REV-456";

        _accountingGateway.ReverseRevenueAsync(
                data.CorrelationId,
                data.ReturnRequestId,
                data.RefundAmount,
                data.Currency,
                data.ReturnedItems,
                Arg.Any<CancellationToken>())
            .Returns(expectedReversalId);

        var result = await _step.ExecuteAsync(data, context, CancellationToken.None);

        Assert.IsType<Completed>(result);
        Assert.Equal(expectedReversalId, ((Completed)result).Data?["RevenueReversalId"]);
        Assert.Equal(data.RefundAmount, ((Completed)result).Data?["Amount"]);
        Assert.Equal(expectedReversalId, context.RevenueReversalId);

        await _accountingGateway.Received(1).ReverseRevenueAsync(
            data.CorrelationId,
            data.ReturnRequestId,
            data.RefundAmount,
            data.Currency,
            data.ReturnedItems,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotRecordTheRefundCashLeg()
    {
        // Payment owns the cash leg: it emits RefundIssuedEvent and the ledger posts
        // refund:{refundId} from that. Recording it here too would double-book.
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RefundId = "REF-123" };

        _accountingGateway.ReverseRevenueAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<string>(),
            Arg.Any<List<OrderItemDto>>(),
            Arg.Any<CancellationToken>()).Returns("REV-OK");

        var result = await _step.ExecuteAsync(data, context, CancellationToken.None);

        Assert.IsType<Completed>(result);
        Assert.DoesNotContain("JournalEntryId", ((Completed)result).Data!.Keys);

        // IAccountingGateway no longer exposes RecordRefund at all, so the only way to
        // reintroduce the double-book is to put it back on the interface.
        Assert.DoesNotContain(
            typeof(IAccountingGateway).GetMethods(),
            m => m.Name.Contains("RecordRefund", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailure_WhenRefundIdIsMissing()
    {
        // RefundId is no longer sent anywhere, but its presence still proves the refund settled
        // before revenue gets reversed.
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RefundId = null };

        var result = await _step.ExecuteAsync(data, context, CancellationToken.None);

        Assert.IsType<Fail>(result);
        Assert.Contains("RefundId is required", ((Fail)result).Reason);

        await _accountingGateway.DidNotReceiveWithAnyArgs().ReverseRevenueAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<string>(),
            Arg.Any<List<OrderItemDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailure_WhenReverseRevenueFails()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RefundId = "REF-123" };
        var errorMessage = "Revenue reversal failed - period closed";

        _accountingGateway.ReverseRevenueAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<string>(),
            Arg.Any<List<OrderItemDto>>(),
            Arg.Any<CancellationToken>()
        ).Throws(new Exception(errorMessage));

        var result = await _step.ExecuteAsync(data, context, CancellationToken.None);

        Assert.IsType<Fail>(result);
        Assert.Contains(errorMessage, ((Fail)result).Reason);
        Assert.Null(context.RevenueReversalId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailure_WhenReturnRequestIdIsMissing()
    {
        var data = CreateSampleData();
        data.ReturnRequestId = Guid.Empty;
        var context = new ReturnSagaContext { RefundId = "REF-123" };

        var result = await _step.ExecuteAsync(data, context, CancellationToken.None);

        Assert.IsType<Fail>(result);
        Assert.Contains("ReturnRequestId is required", ((Fail)result).Reason);

        await _accountingGateway.DidNotReceiveWithAnyArgs().ReverseRevenueAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<string>(),
            Arg.Any<List<OrderItemDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassCorrectParameters_ToReverseRevenue()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RefundId = "REF-SPECIFIC" };

        _accountingGateway.ReverseRevenueAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<string>(),
            Arg.Any<List<OrderItemDto>>(),
            Arg.Any<CancellationToken>()
        ).Returns("REV-OK");

        await _step.ExecuteAsync(data, context, CancellationToken.None);

        await _accountingGateway.Received(1).ReverseRevenueAsync(
            Arg.Is<Guid>(id => id == data.CorrelationId),
            Arg.Is<Guid>(id => id == data.ReturnRequestId),
            Arg.Is<decimal>(amt => amt == 250.50m),
            Arg.Is<string>(curr => curr == "EUR"),
            Arg.Any<List<OrderItemDto>>(),
            Arg.Any<CancellationToken>());
    }
    
    #endregion

    #region CompensateAsync tests

    [Fact]
    public async Task CompensateAsync_ShouldCancelRevenueReversal_WhenReversalIdExists()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RevenueReversalId = "RevToCancel" };

        await _step.CompensateAsync(data, context, CancellationToken.None);

        await _accountingGateway.Received(1).CancelRevenueReversalAsync(
            Arg.Is<string>(id => id == "RevToCancel"),
            Arg.Is<string>(reason => reason.Contains("compensation")),
            Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CompensateAsync_ShouldDoNothing_WhenReversalIdNull()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RevenueReversalId = null };

        await _step.CompensateAsync(data, context, CancellationToken.None);
        
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("No revenue reversal to cancel")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
        
    }
    
    [Fact]
    public async Task CompensateAsync_ShouldDoNothing_WhenReversalIdIsEmptyString()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RevenueReversalId = "" };

        await _step.CompensateAsync(data, context, CancellationToken.None);

        await _accountingGateway.DidNotReceiveWithAnyArgs().CancelRevenueReversalAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompensateAsync_ShouldLogError_WhenCancellationFails()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RevenueReversalId = "REV-123 " };
        
        _accountingGateway.CancelRevenueReversalAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Throws(new Exception("Revenue already finalized"));

        await _step.CompensateAsync(data, context, CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Manual accounting adjustment may be required")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task CompensateAsync_ShouldNotThrow_WhenCancellationFails()
    {
        var data = CreateSampleData();
        var context = new ReturnSagaContext { RevenueReversalId = "Rev123" };

        _accountingGateway.CancelRevenueReversalAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Throws(new Exception("Boom!"));

        var exception = await Record.ExceptionAsync(async () =>
            await _step.CompensateAsync(data, context, CancellationToken.None));
        
        Assert.Null(exception);

    }

    #endregion

    private ReturnSagaData CreateSampleData()
    {
        return new ReturnSagaData
        {
            CorrelationId = Guid.NewGuid(),
            ReturnRequestId = Guid.NewGuid(),
            RefundAmount = 250.50m,
            Currency = "EUR",
            ReturnReason = "Defective Product",
            CustomerId = Guid.NewGuid(),
            ReturnedItems = new List<OrderItemDto>
            {
                new(Guid.NewGuid(), 2, 125.25m, "EUR")
            }
        };
    }
}