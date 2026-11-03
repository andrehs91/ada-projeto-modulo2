using System.Reflection;
using System.Text.Json.Serialization;
using api_simulador_habitacional.Services;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddSingleton<ISimulacaoService, SimulacaoService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "API Simulador Habitacional",
        Version = "v1",
        Description = "Simulações de financiamento habitacional nos sistemas SAC e PRICE e comparação de amortizações extraordinárias. " +
            "Modelo mensal estimado, sem persistência e sem seguros, tarifas ou indexadores calculados separadamente."
    });
    options.SupportNonNullableReferenceTypes();
    options.UseAllOfToExtendReferenceSchemas();
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"), includeControllerXmlComments: true);
});
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("v1/swagger.json", "API Simulador Habitacional v1");
        options.DocumentTitle = "API Simulador Habitacional — Swagger";
    });
}

// Verificação simples compartilhada por Liveness, Readiness e Startup.
app.UseHealthChecks("/health");

app.UseAuthorization();
app.MapControllers();
app.Run();

// Permite hospedar a API em testes de integração.
public partial class Program { }
