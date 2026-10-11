using Doggo.Domain.Cases;

namespace Doggo.Domain.Tests;

public class RollRoomStateMachineTests
{
    public static TheoryData<RollRoomStatus, RollRoomStatus> ValidTransitions => new()
    {
        { RollRoomStatus.Creating, RollRoomStatus.Running },
        { RollRoomStatus.Running, RollRoomStatus.Drawing },
        { RollRoomStatus.Drawing, RollRoomStatus.Drawn },
        { RollRoomStatus.Drawn, RollRoomStatus.Claiming },
        { RollRoomStatus.Drawn, RollRoomStatus.Finished }, // winnerless rooms close at once
        { RollRoomStatus.Claiming, RollRoomStatus.Finished }
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void CanTransition_AllowsTheLinearChain(RollRoomStatus from, RollRoomStatus to)
    {
        Assert.True(RollRoomStateMachine.CanTransition(from, to));
        RollRoomStateMachine.RequireTransition(from, to);
    }

    [Fact]
    public void CanTransition_RejectsEverySkippingOrBackwardMove()
    {
        var statuses = (RollRoomStatus[])Enum.GetValues(typeof(RollRoomStatus));
        var valid = ValidTransitions.Select(t => (t[0], t[1])).ToHashSet();
        var rejected = 0;

        foreach (var from in statuses)
        {
            foreach (var to in statuses)
            {
                if (valid.Contains((from, to)))
                {
                    continue;
                }

                Assert.False(RollRoomStateMachine.CanTransition(from, to), $"{from} → {to} must be rejected.");
                Assert.Throws<InvalidOperationException>(() => RollRoomStateMachine.RequireTransition(from, to));
                rejected++;
            }
        }

        // 6 statuses x 6 targets minus the 5 legal moves.
        Assert.Equal(statuses.Length * statuses.Length - ValidTransitions.Count, rejected);
    }

    [Theory]
    [InlineData(RollRoomStatus.Creating, false)]
    [InlineData(RollRoomStatus.Running, false)]
    [InlineData(RollRoomStatus.Drawing, false)]
    [InlineData(RollRoomStatus.Drawn, false)]
    [InlineData(RollRoomStatus.Claiming, false)]
    [InlineData(RollRoomStatus.Finished, true)]
    public void IsTerminal_OnlyForFinished(RollRoomStatus status, bool expected)
    {
        Assert.Equal(expected, RollRoomStateMachine.IsTerminal(status));
    }
}
