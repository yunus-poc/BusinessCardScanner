using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessCard.Application.Contacts;
using BusinessCard.Application.Errors;
using BusinessCard.Domain.Contacts;
using BusinessCard.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCard.IntegrationTests;

public sealed class ContactsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly object Contact = new
    {
        firstName = "Ahmet",
        lastName = "Yılmaz",
        companyName = "ACME Technology",
        email = "ahmet.yilmaz@acme.com.tr",
        additionalEmails = new[] { "satis@acme.com.tr" },
        mobilePhone = "0532 123 45 67",
        city = "İstanbul",
    };

    [Fact]
    public async Task Create_returns_201_with_the_outlook_id()
    {
        var graph = new FakeGraph();

        var response = await Client(graph).PostAsJsonAsync("/api/contacts", Contact);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("graph-id-1", body.GetProperty("id").GetString());
        Assert.Equal("Ahmet Yılmaz", body.GetProperty("displayName").GetString());

        var saved = graph.Created.Single();
        Assert.Equal(["ahmet.yilmaz@acme.com.tr", "satis@acme.com.tr"], saved.Emails);
        Assert.Equal("+90 532 123 45 67", saved.MobilePhone);
    }

    [Fact]
    public async Task Invalid_contact_is_rejected_before_graph_is_called()
    {
        var graph = new FakeGraph();

        var response = await Client(graph).PostAsJsonAsync("/api/contacts", new { city = "İstanbul", email = "ahmet@" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await Code(response));
        Assert.Empty(graph.Created);
    }

    [Fact]
    public async Task Graph_throttling_reaches_the_app_as_retryable_429_with_retry_after()
    {
        var graph = new FakeGraph
        {
            Failure = new AppException(ErrorCodes.GraphRateLimited, "Outlook is busy.") { RetryAfter = TimeSpan.FromSeconds(20) },
        };

        var response = await Client(graph).PostAsJsonAsync("/api/contacts", Contact);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(20), response.Headers.RetryAfter?.Delta);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ErrorCodes.GraphRateLimited, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task Missing_permission_is_a_non_retryable_403()
    {
        var graph = new FakeGraph { Failure = new AppException(ErrorCodes.GraphAuthorizationFailed, "Not allowed.") };

        var response = await Client(graph).PostAsJsonAsync("/api/contacts", Contact);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.GraphAuthorizationFailed, await Code(response));
    }

    [Fact]
    public async Task Update_writes_to_the_given_contact()
    {
        var graph = new FakeGraph();

        var response = await Client(graph).PutAsJsonAsync("/api/contacts/AAMkAD-123_x=", Contact);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AAMkAD-123_x=", graph.UpdatedIds.Single());
    }

    [Fact]
    public async Task Update_with_a_malformed_id_is_a_validation_error()
    {
        var graph = new FakeGraph();

        var response = await Client(graph).PutAsJsonAsync("/api/contacts/bad%20id", Contact);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await Code(response));
        Assert.Empty(graph.UpdatedIds);
    }

    [Fact]
    public async Task Duplicate_check_reports_matches_with_reasons()
    {
        var graph = new FakeGraph();
        graph.Candidates.Add(new ExistingContact
        {
            Id = "existing-1",
            DisplayName = "Ahmet Yılmaz",
            FirstName = "Ahmet",
            LastName = "Yılmaz",
            CompanyName = "ACME",
            Emails = ["AHMET.YILMAZ@acme.com.tr"],
            MobilePhone = "05321234567",
        });

        var response = await Client(graph).PostAsJsonAsync("/api/contacts/check-duplicate", Contact);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("possibleDuplicate").GetBoolean());
        var match = body.GetProperty("matches")[0];
        Assert.Equal("existing-1", match.GetProperty("id").GetString());
        Assert.Equal(
            ["email", "mobilePhone", "nameAndCompany"],
            match.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Duplicate_check_without_matches_is_a_plain_200()
    {
        var response = await Client(new FakeGraph()).PostAsJsonAsync("/api/contacts/check-duplicate", Contact);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("possibleDuplicate").GetBoolean());
        Assert.Equal(0, body.GetProperty("matches").GetArrayLength());
    }

    [Fact]
    public async Task Duplicate_check_validates_like_save()
    {
        var response = await Client(new FakeGraph()).PostAsJsonAsync("/api/contacts/check-duplicate", new { email = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Contacts_require_authentication()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/contacts", Contact);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient Client(FakeGraph graph)
    {
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IMicrosoftGraphContactService>(graph)));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create());
        return client;
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private sealed class FakeGraph : IMicrosoftGraphContactService
    {
        public AppException? Failure { get; init; }
        public List<ContactDetails> Created { get; } = [];
        public List<string> UpdatedIds { get; } = [];
        public List<ExistingContact> Candidates { get; } = [];

        public Task<IReadOnlyList<ExistingContact>> FindCandidatesAsync(ContactDetails contact, CancellationToken cancellationToken) =>
            Failure is not null ? throw Failure : Task.FromResult<IReadOnlyList<ExistingContact>>(Candidates);

        public Task<SavedContact> CreateAsync(ContactDetails contact, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }
            Created.Add(contact);
            return Task.FromResult(new SavedContact("graph-id-1", contact.DisplayName));
        }

        public Task<SavedContact> UpdateAsync(string contactId, ContactDetails contact, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }
            UpdatedIds.Add(contactId);
            return Task.FromResult(new SavedContact(contactId, contact.DisplayName));
        }
    }
}
