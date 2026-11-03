using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using api_simulador_habitacional.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ApiSimuladorHabitacional.Tests;

public sealed class SimulacaoApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Development");
}

[Trait("Category", "Integration")]
public sealed class SimulacoesIntegrationTests : IClassFixture<SimulacaoApiFactory>, IDisposable
{
    private readonly SimulacaoApiFactory factory;
    private readonly HttpClient client;
    private static readonly JsonSerializerOptions ResponseOptions = CreateResponseOptions();

    public SimulacoesIntegrationTests(SimulacaoApiFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public void Dispose() => client.Dispose();

    [Theory]
    [InlineData("SAC", SistemaAmortizacao.SAC)]
    [InlineData("PRICE", SistemaAmortizacao.PRICE)]
    public async Task Financing_AcceptsAnonymousPayloadWithStringEnum(string system, SistemaAmortizacao expected)
    {
        using var response = await client.PostAsJsonAsync("/api/simulacoes/financiamento", new
        {
            saldoDevedor = 1000m, parcelasRestantes = 4, cetAnual = .12m, sistemaAmortizacao = system
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<FinanciamentoResponse>(content, ResponseOptions);
        Assert.NotNull(body);
        Assert.Equal(expected, body.SistemaAmortizacao);
        Assert.Equal(.12m, body.CetAnual);
        Assert.Equal(4, body.QuantidadeParcelas);
        ScheduleAssertions.Valid(body, 1000m, 4);
        var json = JsonSerializer.Deserialize<JsonElement>(content);
        Assert.Equal(system, json.GetProperty("sistemaAmortizacao").GetString());
    }

    [Theory]
    [InlineData("SAC", "ReduzirPrazo", ObjetivoAmortizacao.ReduzirPrazo)]
    [InlineData("PRICE", "ReduzirPrazo", ObjetivoAmortizacao.ReduzirPrazo)]
    [InlineData("SAC", "ReduzirPrestacao", ObjetivoAmortizacao.ReduzirPrestacao)]
    [InlineData("PRICE", "ReduzirPrestacao", ObjetivoAmortizacao.ReduzirPrestacao)]
    public async Task Amortization_AcceptsAnonymousPayloadAndReturnsComparison(string system, string goal,
        ObjetivoAmortizacao expected)
    {
        using var response = await client.PostAsJsonAsync("/api/simulacoes/amortizacao", new
        {
            saldoDevedor = 1000m, parcelasRestantes = 4, cetAnual = .12m,
            sistemaAmortizacao = system, objetivoAmortizacao = goal, valorAmortizacao = 300m
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<AmortizacaoResponse>(content, ResponseOptions);
        Assert.NotNull(body);
        Assert.Equal(expected, body.ObjetivoAmortizacao);
        Assert.Equal(300m, body.ValorAmortizacao);
        Assert.Equal(Enum.Parse<SistemaAmortizacao>(system), body.SimulacaoAtual.SistemaAmortizacao);
        Assert.Equal(body.SimulacaoAtual.SistemaAmortizacao, body.SimulacaoAposAmortizacao.SistemaAmortizacao);
        ScheduleAssertions.Comparison(body, 1000m, 4);
        if (expected == ObjetivoAmortizacao.ReduzirPrestacao)
            Assert.Equal(4, body.SimulacaoAposAmortizacao.QuantidadeParcelas);
        else
            Assert.True(body.ParcelasReduzidas > 0);
        var json = JsonSerializer.Deserialize<JsonElement>(content);
        Assert.Equal(goal, json.GetProperty("objetivoAmortizacao").GetString());
    }

    [Theory]
    [InlineData("SAC", "ReduzirPrazo", 0)]
    [InlineData("SAC", "ReduzirPrestacao", 1000)]
    [InlineData("PRICE", "ReduzirPrazo", 1000)]
    [InlineData("PRICE", "ReduzirPrestacao", 0)]
    public async Task Amortization_ZeroRateAndBoundaryContributionsAreValid(string system, string goal, int contribution)
    {
        using var response = await client.PostAsJsonAsync("/api/simulacoes/amortizacao", new
        {
            saldoDevedor = 1000m, parcelasRestantes = 4, cetAnual = 0m,
            sistemaAmortizacao = system, objetivoAmortizacao = goal, valorAmortizacao = contribution
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AmortizacaoResponse>(ResponseOptions);
        Assert.NotNull(body);
        Assert.Equal(0m, body.EconomiaJuros);
        ScheduleAssertions.Comparison(body, 1000m, 4);
    }

    private static Dictionary<string, object?> ValidPayload(bool amortization)
    {
        var payload = new Dictionary<string, object?>
        {
            ["saldoDevedor"] = 1000m, ["parcelasRestantes"] = 4,
            ["cetAnual"] = .12m, ["sistemaAmortizacao"] = "SAC"
        };
        if (amortization)
        {
            payload["objetivoAmortizacao"] = "ReduzirPrazo";
            payload["valorAmortizacao"] = 300m;
        }
        return payload;
    }

    public static IEnumerable<object[]> MissingFields()
    {
        foreach (var endpoint in new[] { "financiamento", "amortizacao" })
        foreach (var field in ValidPayload(endpoint == "amortizacao").Keys)
            yield return new object[] { endpoint, field };
    }

    [Theory]
    [MemberData(nameof(MissingFields))]
    public async Task MissingRequiredField_ReturnsValidationProblem(string endpoint, string field)
    {
        var payload = ValidPayload(endpoint == "amortizacao");
        payload.Remove(field);
        using var response = await client.PostAsJsonAsync($"/api/simulacoes/{endpoint}", payload);
        await AssertValidationProblem(response);
    }

    public static IEnumerable<object[]> InvalidFields()
    {
        var common = new (string Field, object? Value)[]
        {
            ("saldoDevedor", -1m), ("saldoDevedor", 0m), ("saldoDevedor", .001m),
            ("saldoDevedor", 1000.001m), ("saldoDevedor", 1_000_000_000.01m),
            ("saldoDevedor", decimal.MaxValue), ("saldoDevedor", null),
            ("parcelasRestantes", -1), ("parcelasRestantes", 0), ("parcelasRestantes", 481),
            ("parcelasRestantes", 4.5), ("cetAnual", -.01m), ("cetAnual", 10.01m),
            ("cetAnual", "not-a-number"), ("sistemaAmortizacao", "INVALID"),
            ("sistemaAmortizacao", 1), ("sistemaAmortizacao", 2),
            ("sistemaAmortizacao", 999), ("sistemaAmortizacao", "1")
        };
        foreach (var endpoint in new[] { "financiamento", "amortizacao" })
        foreach (var (field, value) in common)
            yield return new object[] { endpoint, field, value! };

        foreach (var value in new object[] { -1m, .001m, 300.001m, 1000.01m, 1_000_000_000.01m })
            yield return new object[] { "amortizacao", "valorAmortizacao", value };
        foreach (var value in new object[] { "INVALID", 1, 2, 999, "1" })
            yield return new object[] { "amortizacao", "objetivoAmortizacao", value };
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task InvalidField_ReturnsBadRequestNotServerError(string endpoint, string field, object? value)
    {
        var payload = ValidPayload(endpoint == "amortizacao");
        payload[field] = value;
        using var response = await client.PostAsJsonAsync($"/api/simulacoes/{endpoint}", payload);
        await AssertValidationProblem(response);
    }

    private static async Task AssertValidationProblem(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        var errors = problem.GetProperty("errors");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.NotEmpty(errors.EnumerateObject());
        Assert.All(errors.EnumerateObject(), error => Assert.NotEmpty(error.Value.EnumerateArray()));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Health_RemainsOkOverHttpWithoutRedirects(string environment)
    {
        using var environmentFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var environmentClient = environmentFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost")
        });
        using var response = await environmentClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("http", response.RequestMessage!.RequestUri!.Scheme);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_InDevelopment_DescribesBothEndpointsAndResponses()
    {
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("3.", document.GetProperty("openapi").GetString());
        Assert.Equal("API Simulador Habitacional", document.GetProperty("info").GetProperty("title").GetString());
        foreach (var endpoint in new[] { "financiamento", "amortizacao" })
        {
            var operation = document.GetProperty("paths").GetProperty($"/api/simulacoes/{endpoint}").GetProperty("post");
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
            Assert.True(operation.GetProperty("requestBody").GetProperty("required").GetBoolean());
            Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
        }

        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var type in new[] { "SimularFinanciamentoRequest", "SimularAmortizacaoRequest",
                     "ParcelaResponse", "FinanciamentoResponse", "AmortizacaoResponse" })
        {
            var schema = schemas.GetProperty(type);
            Assert.False(string.IsNullOrWhiteSpace(schema.GetProperty("description").GetString()));
            Assert.All(schema.GetProperty("properties").EnumerateObject(), property =>
                Assert.False(string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString())));
        }

        var system = schemas.GetProperty("SistemaAmortizacao");
        Assert.Equal("string", system.GetProperty("type").GetString());
        Assert.Equal(new[] { "SAC", "PRICE" }, system.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task SwaggerUi_InDevelopment_IsAvailable()
    {
        using var response = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/index.html")]
    public async Task Swagger_IsNotExposedInProduction(string path)
    {
        using var productionFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var productionClient = productionFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await productionClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}