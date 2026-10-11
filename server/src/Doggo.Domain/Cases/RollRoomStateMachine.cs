namespace Doggo.Domain.Cases;

/// <summary>
/// Roll room lifecycle: 创建中→进行中→开奖中→开奖结束→领奖→结束.
/// Linear except for two shortcuts: a draw with no eligible winner moves the room from
/// Drawing to Drawn and closes it straight to Finished (nothing left to claim), and a
/// winner claims through Claiming → Finished.
/// </summary>
public static class RollRoomStateMachine
{
    private static readonly IReadOnlyDictionary<RollRoomStatus, RollRoomStatus[]> Map =
        new Dictionary<RollRoomStatus, RollRoomStatus[]>
        {
            [RollRoomStatus.Creating] = [RollRoomStatus.Running],
            [RollRoomStatus.Running] = [RollRoomStatus.Drawing],
            [RollRoomStatus.Drawing] = [RollRoomStatus.Drawn],
            [RollRoomStatus.Drawn] = [RollRoomStatus.Claiming, RollRoomStatus.Finished],
            [RollRoomStatus.Claiming] = [RollRoomStatus.Finished]
        };

    public static bool CanTransition(RollRoomStatus from, RollRoomStatus to) =>
        Map.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void RequireTransition(RollRoomStatus from, RollRoomStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Illegal roll room transition {from} → {to}.");
        }
    }

    public static bool IsTerminal(RollRoomStatus status) => status == RollRoomStatus.Finished;
}
