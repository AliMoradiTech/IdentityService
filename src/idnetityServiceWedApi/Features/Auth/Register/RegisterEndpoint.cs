using FluentValidation;
using idnetityServiceWedApi.Data;
using idnetityServiceWedApi.Contracts;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace idnetityServiceWedApi.Features.Auth.Register;

public static class RegisterEndpoint
{
    public static void MapRegisterEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", async (
            RegisterRequest request,
            IValidator<RegisterRequest> validator,
            UserManager<ApplicationUser> userManager,
            AuthDbContext db) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
            };

            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

                return Results.ValidationProblem(errors);
            }

            var integrationEvent = new UserRegisteredEvent(Guid.NewGuid(), user.Id, user.Email!, user.UserName!, null, DateTimeOffset.UtcNow);
            db.OutboxMessages.Add(new OutboxMessage { EventId = integrationEvent.EventId, Type = nameof(UserRegisteredEvent), Payload = JsonSerializer.Serialize(integrationEvent), OccurredAt = integrationEvent.OccurredAt, TraceParent = Activity.Current?.Id, TraceState = Activity.Current?.TraceStateString });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Created($"/api/auth/users/{user.Id}", new { user.Id, user.Email });
        });
    }
}
