using Doggo.Domain.Cases;

namespace Doggo.Domain.Tests;

public class SeatStateMachineTests
{
    public static TheoryData<SeatStatus, SeatStatus> ValidTransitions => new()
    {
        { SeatStatus.OnSale, SeatStatus.Locked },
        { SeatStatus.Locked, SeatStatus.OnSale },
        { SeatStatus.Locked, SeatStatus.Paid },
        { SeatStatus.Paid, SeatStatus.Opening },
        { SeatStatus.Opening, SeatStatus.Paid },
        { SeatStatus.Opening, SeatStatus.Opened },
        { SeatStatus.Opened, SeatStatus.FetchingStock },
        { SeatStatus.FetchingStock, SeatStatus.StockReady },
        { SeatStatus.StockReady, SeatStatus.Trading },
        { SeatStatus.Trading, SeatStatus.Completed }
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void Allows_the_legacy_chain_and_its_two_back_edges(SeatStatus from, SeatStatus to)
    {
        Assert.True(SeatStateMachine.CanTransition(from, to));
        SeatStateMachine.RequireTransition(from, to);
    }

    [Fact]
    public void Rejects_skipping_and_backwards_moves()
    {
        var from = Enum.GetValues<SeatStatus>();
        var invalid = 0;

        foreach (var a in from)
        {
            foreach (var b in from)
            {
                if (a == b)
                {
                    Assert.False(SeatStateMachine.CanTransition(a, a));
                    invalid++;
                    continue;
                }

                if (SeatStateMachine.CanTransition(a, b))
                {
                    continue;
                }

                invalid++;
                Assert.Throws<InvalidOperationException>(() => SeatStateMachine.RequireTransition(a, b));
            }
        }

        // 9x9 grid minus the 10 legal transitions.
        Assert.Equal(from.Length * from.Length - 10, invalid);
    }

    [Fact]
    public void Only_completed_is_terminal()
    {
        foreach (var status in Enum.GetValues<SeatStatus>())
        {
            Assert.Equal(status == SeatStatus.Completed, SeatStateMachine.IsTerminal(status));
        }
    }
}
