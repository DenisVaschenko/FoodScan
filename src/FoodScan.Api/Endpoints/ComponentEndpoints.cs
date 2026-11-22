using System.Security.Claims;
using FoodScan.Api.Data;
using FoodScan.Api.Domain;
using FoodScan.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodScan.Api.Endpoints;

public record TrackRequest(TrackingReason? Reason, string? Note);

public static class ComponentEndpoints
{
    public static void MapComponents(this WebApplication app)
    {
        app.MapGet("/api/components", async (string? q, int? limit, IComponentSearchService search, CancellationToken ct) =>
        {
            if (q?.Length > 100)
            {
                return Results.BadRequest(new
                {
                    message = "Пошуковий запит має містити до 100 символів."
                });
            }

            int count = limit ?? 10;

            if (count < 1 || count > 10)
            {
                return Results.BadRequest(new
                {
                    message = "Кількість результатів має бути від 1 до 10."
                });
            }

            var results = await search.SearchAsync(q ?? "", count, ct);
            return Results.Ok(results);
        }).RequireAuthorization().WithTags("Components");

        var group = app.MapGroup("/api/me/components").RequireAuthorization().WithTags("Tracking");
        group.MapGet("", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await (from t in db.TrackedComponents where t.UserId == user.UserId()
                join c in db.Components on t.ComponentId equals c.Id
                orderby c.Name select new { t.ComponentId, c.Name, t.Reason, t.Note }).ToListAsync(ct)));

        group.MapPut("/{componentId:int}", async (int componentId, TrackRequest r, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (r.Reason is null || !Enum.IsDefined(r.Reason.Value) || r.Note?.Length > 300)
                throw new ApiException(400, "invalid_tracking", "Некоректна причина або примітка довша за 300 символів.");
            if (!await db.Components.AnyAsync(x => x.Id == componentId, ct)) return Results.NotFound();
            var userId = user.UserId();
            var tracked = await db.TrackedComponents.FindAsync([userId, componentId], ct);
            if (tracked is null)
            {
                tracked = new() { UserId = userId, ComponentId = componentId };
                db.TrackedComponents.Add(tracked);
            }
            tracked.Reason = r.Reason.Value;
            tracked.Note = r.Note?.Trim() ?? "";
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                throw new ApiException(409, "tracking_conflict", "Список було змінено одночасно. Повторіть запит.");
            }
            return Results.Ok(new { tracked.ComponentId, tracked.Reason, tracked.Note });
        });
        group.MapDelete("/{componentId:int}", async (int componentId, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            await db.TrackedComponents.Where(x => x.UserId == user.UserId() && x.ComponentId == componentId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}
