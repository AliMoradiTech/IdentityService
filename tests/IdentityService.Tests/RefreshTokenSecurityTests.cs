using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using idnetityServiceWedApi.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IdentityService.Tests;

public sealed class RefreshTokenSecurityTests(IdentityServiceFactory factory)
    : IClassFixture<IdentityServiceFactory>
{
    [Fact]
    public async Task Refresh_token_is_rejected_after_security_stamp_changes()
    {
        (string email, string refreshToken) = await RegisterAndGetRefreshTokenAsync();

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = (await users.FindByEmailAsync(email)).ShouldNotBeNull();
            (await users.UpdateSecurityStampAsync(user)).Succeeded.ShouldBeTrue();
        }

        HttpResponseMessage response = await RefreshAsync(refreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_grant");
    }

    [Fact]
    public async Task Refresh_token_is_rejected_for_locked_out_user()
    {
        (string email, string refreshToken) = await RegisterAndGetRefreshTokenAsync();

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = (await users.FindByEmailAsync(email)).ShouldNotBeNull();
            (await users.SetLockoutEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
            (await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10))).Succeeded.ShouldBeTrue();
        }

        HttpResponseMessage response = await RefreshAsync(refreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_grant");
    }

    [Fact]
    public async Task Development_configuration_does_not_rate_limit_token_requests()
    {
        HttpClient client = factory.CreateClient();
        var requests = Enumerable.Range(0, 40).Select(_ => client.PostAsync("/api/auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "invalid" })));

        HttpResponseMessage[] responses = await Task.WhenAll(requests);

        responses.ShouldAllBe(response => response.StatusCode != HttpStatusCode.TooManyRequests);
    }

    private async Task<(string Email, string RefreshToken)> RegisterAndGetRefreshTokenAsync()
    {
        HttpClient client = factory.CreateClient();
        string email = $"user-{Guid.NewGuid():N}@example.test";
        HttpResponseMessage registration = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "TestPass123" });
        registration.StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage token = await client.PostAsync("/api/auth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = email,
                ["password"] = "TestPass123",
                ["scope"] = "offline_access email profile"
            }));
        string tokenBody = await token.Content.ReadAsStringAsync();
        token.StatusCode.ShouldBe(HttpStatusCode.OK, tokenBody);
        using JsonDocument payload = JsonDocument.Parse(tokenBody);
        return (email, payload.RootElement.GetProperty("refresh_token").GetString().ShouldNotBeNull());
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        factory.CreateClient().PostAsync("/api/auth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }));
}
