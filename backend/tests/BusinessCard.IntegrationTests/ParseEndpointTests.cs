using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BusinessCard.Application.Errors;
using BusinessCard.IntegrationTests.Infrastructure;

namespace BusinessCard.IntegrationTests;

public sealed class ParseEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Endpoint = "/api/business-cards/parse";

    private static readonly object SampleCard = new
    {
        lines = new object[]
        {
            new { text = "ACME TECHNOLOGY", order = 1, region = "top", size = "large", align = "center" },
            new { text = "Ahmet Yılmaz", order = 2, region = "center", size = "large", gap = "large" },
            new { text = "E: ahmet.yilmaz@acme.com.tr", order = 3, region = "bottom", size = "small" },
            new { text = "M: 0532 123 45 67", order = 4, region = "bottom", size = "small", sameBlock = true },
            new { text = "www.acme.com.tr", order = 5, region = "bottom", size = "small" },
        },
    };

    [Fact]
    public async Task Parses_a_card_into_field_values_with_confidence()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(Endpoint, SampleCard);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var contact = body.GetProperty("contact");

        var email = contact.GetProperty("email");
        Assert.Equal("ahmet.yilmaz@acme.com.tr", email.GetProperty("value").GetString());
        Assert.True(email.GetProperty("confidence").GetDouble() > 0.9);
        Assert.False(email.GetProperty("needsReview").GetBoolean());

        Assert.Equal("+90 532 123 45 67", contact.GetProperty("mobilePhone").GetProperty("value").GetString());
        Assert.Equal("https://www.acme.com.tr", contact.GetProperty("website").GetProperty("value").GetString());
        // No model in tests: the layout fallback fills the name and flags it.
        var firstName = contact.GetProperty("firstName");
        Assert.Equal("Ahmet", firstName.GetProperty("value").GetString());
        Assert.True(firstName.GetProperty("needsReview").GetBoolean());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("additionalEmails").ValueKind);
        Assert.False(body.GetProperty("meta").GetProperty("llmUsed").GetBoolean());
    }

    [Fact]
    public async Task Minimal_line_shape_from_the_spec_is_accepted()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(Endpoint, new
        {
            lines = new[] { new { text = "Ahmet Yılmaz", order = 1, region = "center", size = "large" } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Endpoint, SampleCard);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static TheoryData<string, string> InvalidBodies() => new()
    {
        { "no lines", """{"lines":[]}""" },
        { "missing lines", "{}" },
        { "missing region", """{"lines":[{"text":"a","order":1,"size":"normal"}]}""" },
        { "unknown region", """{"lines":[{"text":"a","order":1,"region":"middle","size":"normal"}]}""" },
        { "duplicate order", """{"lines":[{"text":"a","order":1,"region":"top","size":"normal"},{"text":"b","order":1,"region":"top","size":"normal"}]}""" },
        { "blank text", """{"lines":[{"text":"  ","order":1,"region":"top","size":"normal"}]}""" },
        { "malformed json", """{"lines":[""" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_requests_return_validation_problem(string scenario, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await factory.CreateAuthenticatedClient().PostAsync(Endpoint, content);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario}: {response.StatusCode}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Line_errors_are_keyed_by_json_path()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(Endpoint, new
        {
            lines = new object[]
            {
                new { text = "ok", order = 1, region = "top", size = "normal" },
                new { text = "no size", order = 2, region = "top" },
            },
        });

        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("lines[1].size", out _));
    }
}
