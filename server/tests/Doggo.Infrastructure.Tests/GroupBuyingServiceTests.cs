using System.Net;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Cases;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Tests;

public class GroupBuyingServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private sealed class StubHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder { get; init; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await Responder(request);
        }
    }

    private static DoggoDbContext CreateDb(string? name = null) =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase(name ?? $"group-buying-tests-{Guid.NewGuid():N}")
            .Options);

    /// <summary>Lets a test fail one write with a plain database outage (not a lock conflict).</summary>
    private sealed class OutageDbContext(DbContextOptions<DoggoDbContext> options) : DoggoDbContext(options)
    {
        public bool ThrowOnNextSave { get; set; }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (ThrowOnNextSave)
            {
                ThrowOnNextSave = false;
                throw new InvalidOperationException("simulated database outage");
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private static (GroupBuyingService Service, StubHandler Handler) CreateService(
        DoggoDbContext db,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? vaporResponder = null,
        bool vaporConfigured = true)
    {
        var handler = new StubHandler
        {
            Responder = vaporResponder ?? VaporStub()
        };

        var options = vaporConfigured
            ? new VaporOptions { BaseUrl = "http://vapor.test", ApiKey = "key", DefaultTarget = "steam1", JobPollIntervalMilliseconds = 10, JobTimeoutSeconds = 5 }
            : new VaporOptions();

        var client = new VaporClient(new HttpClient(handler) { BaseAddress = new Uri("http://vapor.test/") }, Options.Create(options));
        return (new GroupBuyingService(db, client, NullLogger<GroupBuyingService>.Instance), handler);
    }

    private static HttpResponseMessage Response(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string JobJson(Guid jobId, string status, string? error = null) =>
        JsonSerializer.Serialize(new
        {
            job = new
            {
                id = jobId,
                action = "case_open",
                status,
                createdAt = "2026-10-11T12:00:00Z"
            },
            tasks = new object[]
            {
                new { id = Guid.NewGuid(), jobId, action = "case_open", status, target = "steam1", attempt = 1, output = (object?)null, error }
            }
        });

    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> VaporStub(
        string status = "finished",
        string? error = null,
        List<string>? capturedBodies = null)
    {
        var jobId = Guid.NewGuid();
        var json = JobJson(jobId, status, error);

        return async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                capturedBodies?.Add(await request.Content.ReadAsStringAsync());
                return Response(HttpStatusCode.Accepted, JsonSerializer.Serialize(new { job = new { id = jobId, action = "case_open", status = "queued", createdAt = "2026-10-11T12:00:00Z" } }));
            }

            return Response(HttpStatusCode.OK, json);
        };
    }

    private static async Task<Product> SeedCrateAsync(DoggoDbContext db)
    {
        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Type = ProductType.Crate,
            Name = "Snakebite Case",
            ClassId = "493",
            PurchasePriceCents = 8000,
            ReferencePriceCents = 9000,
            AdjustPriceCents = 1000,
            FinalPriceCents = 10000,
            RegularPriceCents = 12000
        };
        db.Products.Add(product);
        return product;
    }

    private static async Task<UserOrder> SeedPaidOrderAsync(DoggoDbContext db, Guid productId, ServiceMode mode = ServiceMode.CloudOpen)
    {
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            OrderNo = $"DO{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100000, 999999)}",
            ProductId = productId,
            Status = OrderStatus.PendingSettlement,
            ServiceMode = mode
        };
        db.UserOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<GroupBuying> SeedBatchAsync(DoggoDbContext db, int seatCount = 2)
    {
        var product = await SeedCrateAsync(db);
        await db.SaveChangesAsync();

        var batch = new GroupBuying
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Lot = $"lot-{Guid.NewGuid():N}",
            VaporCaseId = "case-1",
            SeatCount = seatCount,
            Version = 1
        };
        db.GroupBuyings.Add(batch);
        for (var i = 1; i <= seatCount; i++)
        {
            db.GroupBuyingSeats.Add(new GroupBuyingSeat
            {
                Id = Guid.CreateVersion7(),
                GroupBuyingId = batch.Id,
                SeatNo = i,
                CrateAssetId = $"crate-{i}",
                KeyAssetId = $"key-{i}"
            });
        }

        await db.SaveChangesAsync();
        return batch;
    }

    private static async Task<UserOrder> SeedValidOrderAsync(DoggoDbContext db, Guid productId, Guid? userId = null)
    {
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = userId ?? UserId,
            OrderNo = $"DO{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100000, 999999)}",
            ProductId = productId,
            Status = OrderStatus.PendingSettlement,
            ServiceMode = ServiceMode.CloudOpen
        };
        db.UserOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static OpeningResultItem DropItem(string assetId = "730-drop-1") => new(
        assetId,
        "493",
        "0",
        "AK-47 | Redline",
        CaseQuality.Classified,
        "Field-Tested",
        0.2312,
        "classified",
        "https://img.example/ak.png",
        "steam://rungame/730/1",
        8800,
        DateTime.UtcNow.AddHours(8));

    [Fact]
    public async Task CreateBatch_creates_ordered_seats_and_starts_version_at_one()
    {
        using var db = CreateDb();
        var product = await SeedCrateAsync(db);
        await db.SaveChangesAsync();
        var (service, _) = CreateService(db);

        var result = await service.CreateBatchAsync(new CreateBatchCommand(
            product.Id, "lot-42", "case-42",
            [new BatchSeatInput("crate-1", "key-1"), new BatchSeatInput("crate-2", "key-2")]));

        Assert.Equal(2, result.SeatCount);
        var batch = db.GroupBuyings.First(b => b.Id == result.BatchId);
        Assert.Equal(1, batch.Version);
        Assert.Equal(GroupBuyingStatus.OnSale, batch.Status);
        Assert.Equal("case-42", batch.VaporCaseId);

        var seats = await db.GroupBuyingSeats
            .Where(s => s.GroupBuyingId == batch.Id)
            .OrderBy(s => s.SeatNo)
            .ToListAsync();
        Assert.Equal([1, 2], seats.Select(s => s.SeatNo));
        Assert.All(seats, s => Assert.Equal(SeatStatus.OnSale, s.Status));
        Assert.Equal("crate-1", seats[0].CrateAssetId);
        Assert.Equal("key-2", seats[1].KeyAssetId);
    }

    [Fact]
    public async Task CreateBatch_rejects_bad_input_and_duplicate_lots()
    {
        using var db = CreateDb();
        var product = await SeedCrateAsync(db);
        await db.SaveChangesAsync();
        var (service, _) = CreateService(db);

        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.CreateBatchAsync(new CreateBatchCommand(product.Id, " ", "case-1", [new BatchSeatInput("c", null)])));
        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.CreateBatchAsync(new CreateBatchCommand(product.Id, "lot", " ", [new BatchSeatInput("c", null)])));
        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.CreateBatchAsync(new CreateBatchCommand(product.Id, "lot", "case-1", [])));
        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.CreateBatchAsync(new CreateBatchCommand(product.Id, "lot", "case-1", [new BatchSeatInput(" ", null)])));
        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.CreateBatchAsync(new CreateBatchCommand(Guid.CreateVersion7(), "lot", "case-1", [new BatchSeatInput("c", null)])));

        await service.CreateBatchAsync(new CreateBatchCommand(product.Id, "dup-lot", "case-1", [new BatchSeatInput("c", null)]));
        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.CreateBatchAsync(new CreateBatchCommand(product.Id, "dup-lot", "case-1", [new BatchSeatInput("c", null)])));
    }

    [Fact]
    public async Task ClaimSeat_locks_the_lowest_seat_and_binds_the_order()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var order = await SeedPaidOrderAsync(db, batch.ProductId);
        var (service, _) = CreateService(db);

        var claimed = await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));

        Assert.Equal(1, claimed.SeatNo);
        Assert.Equal(1, claimed.SoldCount);
        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1);
        Assert.Equal(SeatStatus.Locked, seat.Status);
        Assert.Equal(order.Id, seat.OrderId);
    }

    [Fact]
    public async Task ClaimSeat_fills_the_batch_then_reports_sold_out()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db, seatCount: 2);
        var (service, _) = CreateService(db);

        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));

        var stored = db.GroupBuyings.First(b => b.Id == batch.Id);
        Assert.Equal(GroupBuyingStatus.SoldOut, stored.Status);
        Assert.Equal(2, stored.SoldCount);

        var soldOutOrder = await SeedValidOrderAsync(db, batch.ProductId);
        await Assert.ThrowsAsync<BatchSoldOutException>(() =>
            service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, soldOutOrder.Id)));
        await Assert.ThrowsAsync<BatchNotFoundException>(() =>
            service.ClaimSeatAsync(new ClaimSeatCommand(Guid.CreateVersion7(), Guid.CreateVersion7())));
    }

    [Fact]
    public async Task ClaimSeat_loses_the_race_when_the_batch_version_moved()
    {
        var store = $"gb-race-{Guid.NewGuid():N}";
        var seed = CreateDb(store);
        var batch = await SeedBatchAsync(seed, seatCount: 1);
        seed.ChangeTracker.Clear();
        await seed.DisposeAsync();

        using var db1 = CreateDb(store);
        using var db2 = CreateDb(store);
        var (service, _) = CreateService(db1);

        // A concurrent worker sells the only seat from its own context first.
        var rival = db2.GroupBuyings.First(b => b.Id == batch.Id);
        var rivalSeat = db2.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id);
        rival.SoldCount++;
        rival.Version++;
        rival.Status = GroupBuyingStatus.SoldOut;
        rivalSeat.Status = SeatStatus.Locked;
        await db2.SaveChangesAsync();

        var racedOrder = await SeedValidOrderAsync(db1, batch.ProductId);
        await Assert.ThrowsAsync<BatchSoldOutException>(() =>
            service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, racedOrder.Id)));
    }

    [Fact]
    public async Task Paid_and_release_roundtrip_between_locked_and_paid()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));

        Assert.Equal(1, await service.MarkSeatPaidAsync(batch.Id, 1, UserId));
        Assert.Equal(SeatStatus.Paid, (db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1)).Status);

        // Release only applies to locked seats.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReleaseSeatAsync(batch.Id, 1, UserId));

        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));
        Assert.Equal(2, await service.ReleaseSeatAsync(batch.Id, 2, UserId));
        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 2);
        Assert.Equal(SeatStatus.OnSale, seat.Status);
        Assert.Null(seat.OrderId);
    }

    [Fact]
    public async Task Paid_loses_the_race_when_the_seat_version_moved()
    {
        var store = $"gb-seat-race-{Guid.NewGuid():N}";
        var seed = CreateDb(store);
        var batch = await SeedBatchAsync(seed);
        var order = await SeedValidOrderAsync(seed, batch.ProductId);
        seed.ChangeTracker.Clear();
        await seed.DisposeAsync();

        using var db = CreateDb(store);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));

        // A concurrent worker with its own context bumps the seat version first.
        using var rivalDb = CreateDb(store);
        var rival = rivalDb.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id);
        rival.Status = SeatStatus.Paid;
        rival.Version++;
        await rivalDb.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<SeatConcurrencyException>(() =>
            service.MarkSeatPaidAsync(batch.Id, 1, UserId));
        Assert.IsType<DbUpdateConcurrencyException>(ex.InnerException);
    }

    [Fact]
    public async Task Seat_conflict_clears_the_context_so_a_retry_in_the_same_scope_works()
    {
        var store = $"gb-seat-retry-{Guid.NewGuid():N}";
        var seed = CreateDb(store);
        var batch = await SeedBatchAsync(seed);
        var order = await SeedValidOrderAsync(seed, batch.ProductId);
        seed.ChangeTracker.Clear();
        await seed.DisposeAsync();

        using var db = CreateDb(store);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));

        // An unrelated concurrent touch bumps the seat version while db still holds the
        // claimed seat (Locked, stale version), so the next save conflicts.
        using var rivalDb = CreateDb(store);
        var rival = rivalDb.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id);
        rival.Version++;
        await rivalDb.SaveChangesAsync();

        await Assert.ThrowsAsync<SeatConcurrencyException>(
            () => service.MarkSeatPaidAsync(batch.Id, 1, UserId));

        // The losing writes were dropped, so the same scope can retry right away: the fresh
        // read (Locked, bumped version) pays without rethrowing the old conflict.
        Assert.All(db.ChangeTracker.Entries(), e => Assert.NotEqual(EntityState.Modified, e.State));
        Assert.Equal(1, await service.MarkSeatPaidAsync(batch.Id, 1, UserId));
    }

    [Fact]
    public async Task Mutations_reject_foreign_user_seats_inside_the_service()
    {
        var otherUserId = Guid.CreateVersion7();
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));

        await Assert.ThrowsAsync<SeatNotFoundException>(() => service.MarkSeatPaidAsync(batch.Id, 1, otherUserId));
        await Assert.ThrowsAsync<SeatNotFoundException>(() => service.ReleaseSeatAsync(batch.Id, 1, otherUserId));
        await Assert.ThrowsAsync<SeatNotFoundException>(() =>
            service.AdvanceSeatAsync(batch.Id, 1, SeatStatus.FetchingStock, otherUserId));
        await Assert.ThrowsAsync<SeatNotFoundException>(() =>
            service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(batch.Id, 1, DropItem(), null, null), otherUserId));
        await Assert.ThrowsAsync<SeatNotFoundException>(() =>
            service.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), otherUserId));
        Assert.Equal(SeatStatus.Locked, (db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1)).Status);
    }

    [Fact]
    public async Task Released_seat_bound_to_a_new_user_rejects_the_previous_owner()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db);
        var oldOrder = await SeedValidOrderAsync(db, batch.ProductId);

        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, oldOrder.Id));
        await service.ReleaseSeatAsync(batch.Id, 1, UserId);

        var newUserId = Guid.CreateVersion7();
        var newOrder = await SeedValidOrderAsync(db, batch.ProductId, newUserId);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, newOrder.Id));

        await Assert.ThrowsAsync<SeatNotFoundException>(() => service.MarkSeatPaidAsync(batch.Id, 1, UserId));
        await Assert.ThrowsAsync<SeatNotFoundException>(() => service.ReleaseSeatAsync(batch.Id, 1, UserId));
        await Assert.ThrowsAsync<SeatNotFoundException>(() =>
            service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(batch.Id, 1, DropItem(), null, null), UserId));

        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1);
        Assert.Equal(newOrder.Id, seat.OrderId);
        Assert.Equal(SeatStatus.Locked, seat.Status);
    }

    [Fact]
    public async Task SubmitOpening_requires_vapor_and_rolls_back_on_failure()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db, vaporConfigured: false);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));

        var ex = await Assert.ThrowsAsync<CaseOpeningUnavailableException>(() =>
            service.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId));
        Assert.Contains("vapor is not configured", ex.Message);
        Assert.Equal(SeatStatus.Locked, (db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1)).Status);

        // Configured vapor, but a failed task: the seat must return to Paid for retry.
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);
        var (failing, _) = CreateService(db, VaporStub(status: "failed", error: "case not found"));
        await Assert.ThrowsAsync<VaporClientException>(() =>
            failing.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId));
        Assert.Equal(SeatStatus.Paid, (db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1)).Status);
    }

    [Fact]
    public async Task SubmitOpening_preserves_the_dispatch_error_when_rollback_conflicts()
    {
        var store = $"gb-open-rollback-{Guid.NewGuid():N}";
        var seed = CreateDb(store);
        var batch = await SeedBatchAsync(seed);
        var order = await SeedValidOrderAsync(seed, batch.ProductId);
        seed.ChangeTracker.Clear();
        await seed.DisposeAsync();

        using var db = CreateDb(store);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);

        // A rival context moves the seat version while vapor is dispatching. The rollback
        // must reload and retry instead of replacing the dispatch failure.
        using var rival = CreateDb(store);
        var (failing, _) = CreateService(db, async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var rivalSeat = await rival.GroupBuyingSeats.FirstAsync(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1);
                rivalSeat.Version++;
                await rival.SaveChangesAsync();
                return Response(HttpStatusCode.Accepted, JsonSerializer.Serialize(new
                {
                    job = new { id = Guid.NewGuid(), action = "case_open", status = "queued", createdAt = "2026-10-11T12:00:00Z" }
                }));
            }

            return Response(HttpStatusCode.OK, JobJson(Guid.NewGuid(), "failed", "case not found"));
        });

        var ex = await Assert.ThrowsAsync<VaporClientException>(() =>
            failing.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId));
        Assert.Contains("case not found", ex.Message);
        Assert.Equal(SeatStatus.Paid, (db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1)).Status);
    }

    [Fact]
    public async Task SubmitOpening_preserves_the_dispatch_error_when_rollback_fails()
    {
        var store = $"gb-open-rollback-outage-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase(store)
            .Options;
        await using var db = new OutageDbContext(options);
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);

        var (failing, _) = CreateService(db, request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Response(HttpStatusCode.Accepted, JsonSerializer.Serialize(new
                {
                    job = new { id = Guid.NewGuid(), action = "case_open", status = "queued", createdAt = "2026-10-11T12:00:00Z" }
                })));
            }

            // The dispatch failed and the rollback's write is about to hit a database outage:
            // the outage must not replace the vapor error, and the seat stays Opening.
            db.ThrowOnNextSave = true;
            return Task.FromResult(Response(HttpStatusCode.OK, JobJson(Guid.NewGuid(), "failed", "case not found")));
        });

        var ex = await Assert.ThrowsAsync<VaporClientException>(() =>
            failing.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId));
        Assert.Contains("case not found", ex.Message);

        // Check the persisted state with a fresh context: the failed rollback must not have
        // moved the seat out of Opening (it stays recoverable for a later paid-open retry).
        await using var verify = CreateDb(store);
        Assert.Equal(SeatStatus.Opening, verify.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1).Status);
    }

    [Fact]
    public async Task SubmitOpening_success_moves_seat_to_opening_and_sends_crate_asset()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);

        var bodies = new List<string>();
        var (success, _) = CreateService(db, VaporStub(capturedBodies: bodies));

        var jobId = await success.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId);

        Assert.NotEqual(Guid.Empty, jobId);
        Assert.Equal(SeatStatus.Opening, (db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1)).Status);

        var body = Assert.Single(bodies);
        using var payload = JsonDocument.Parse(body);
        Assert.Equal("case_open", payload.RootElement.GetProperty("action").GetString());
        Assert.Equal("case-1", payload.RootElement.GetProperty("payload").GetProperty("caseId").GetString());
        Assert.Equal("crate-1", payload.RootElement.GetProperty("payload").GetProperty("crateAssetId").GetString());
        Assert.Equal("key-1", payload.RootElement.GetProperty("payload").GetProperty("keyAssetId").GetString());
    }

    [Fact]
    public async Task SubmitOpening_rejects_incomplete_assets_before_touching_the_seat()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, (await SeedValidOrderAsync(db, batch.ProductId)).Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);

        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1);
        seat.KeyAssetId = null;
        db.GroupBuyings.First(b => b.Id == batch.Id).VaporCaseId = " ";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId));
        Assert.Equal(SeatStatus.Paid, (db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1)).Status);
    }

    [Fact]
    public async Task Opening_result_creates_prize_log_and_marks_seat_opened()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var order = await SeedPaidOrderAsync(db, batch.ProductId);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);
        await service.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId);

        var applied = await service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(
            batch.Id, 1, DropItem(), HistoryId: 4242, RawJson: """{"drop":"raw"}"""), UserId);

        Assert.True(applied.Applied);
        Assert.NotNull(applied.PrizeId);

        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1);
        Assert.Equal(SeatStatus.Opened, seat.Status);
        Assert.Equal("730-drop-1", seat.PrizeAssetId);
        Assert.Equal(4242, seat.HistoryId);

        var prize = db.UserPrizes.First(p => p.OrderId == order.Id);
        Assert.Equal("AK-47 | Redline", prize.MarketHashName);
        Assert.Equal(CaseQuality.Classified, prize.Quality);
        Assert.Equal(PrizeSource.GroupOpen, prize.Source);

        var log = db.UserPrizeLogs.First(l => l.PrizeId == prize.Id);
        Assert.Equal("""{"drop":"raw"}""", log.RawJson);
    }

    [Fact]
    public async Task Opening_result_is_idempotent_per_order_and_asset()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var order = await SeedPaidOrderAsync(db, batch.ProductId);
        var (service, _) = CreateService(db);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);
        await service.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId);

        await service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(batch.Id, 1, DropItem(), null, null), UserId);
        var replay = await service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(batch.Id, 1, DropItem(), null, null), UserId);

        Assert.False(replay.Applied);
        Assert.Equal(1, await db.UserPrizes.CountAsync(p => p.OrderId == order.Id));
    }

    [Fact]
    public async Task Opening_result_rejects_cancelled_orders_and_state_jumps()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var (service, _) = CreateService(db);

        // Cancelled order → no prize.
        var cancelled = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            OrderNo = "DO-CANCELLED",
            Status = OrderStatus.Cancelled,
            ServiceMode = ServiceMode.CloudOpen
        };
        db.UserOrders.Add(cancelled);
        await db.SaveChangesAsync();
        var cancelledSeat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1);
        cancelledSeat.OrderId = cancelled.Id;
        cancelledSeat.Status = SeatStatus.Opened;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<CaseValidationException>(() =>
            service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(batch.Id, 1, DropItem(), null, null), UserId));

        // Prize on a seat that never opened → illegal transition.
        var order = await SeedPaidOrderAsync(db, batch.ProductId);
        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(batch.Id, 2, DropItem(), null, null), UserId));
    }

    [Fact]
    public async Task Full_seat_chain_reaches_completed()
    {
        using var db = CreateDb();
        var batch = await SeedBatchAsync(db);
        var order = await SeedPaidOrderAsync(db, batch.ProductId);
        var (service, _) = CreateService(db);

        await service.ClaimSeatAsync(new ClaimSeatCommand(batch.Id, order.Id));
        await service.MarkSeatPaidAsync(batch.Id, 1, UserId);
        await service.SubmitOpeningAsync(new SubmitOpeningCommand(batch.Id, 1), UserId);
        await service.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(batch.Id, 1, DropItem(), null, null), UserId);
        await service.AdvanceSeatAsync(batch.Id, 1, SeatStatus.FetchingStock, UserId);
        await service.AdvanceSeatAsync(batch.Id, 1, SeatStatus.StockReady, UserId);
        await service.AdvanceSeatAsync(batch.Id, 1, SeatStatus.Trading, UserId);
        await service.AdvanceSeatAsync(batch.Id, 1, SeatStatus.Completed, UserId);

        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batch.Id && s.SeatNo == 1);
        Assert.Equal(SeatStatus.Completed, seat.Status);

        // Jumps along the way are refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdvanceSeatAsync(batch.Id, 1, SeatStatus.FetchingStock, UserId));
    }
}
