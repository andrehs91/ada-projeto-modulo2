using System.ComponentModel.DataAnnotations;

namespace api_simulador_habitacional.Contracts;

/// <summary>Sistema de amortização do financiamento.</summary>
/// <remarks>Enviar o nome textual no JSON; valores numéricos não são aceitos.</remarks>
public enum SistemaAmortizacao
{
    /// <summary>Amortização constante, com prestações normalmente decrescentes.</summary>
    SAC = 1,
    /// <summary>Prestação de referência constante, com amortização crescente e ajuste na última parcela.</summary>
    PRICE = 2
}

/// <summary>Destino da amortização extraordinária.</summary>
/// <remarks>Enviar "ReduzirPrazo" ou "ReduzirPrestacao" no JSON; valores numéricos não são aceitos.</remarks>
public enum ObjetivoAmortizacao
{
    /// <summary>Preserva a quota SAC ou a prestação de referência PRICE para antecipar a quitação.</summary>
    ReduzirPrazo = 1,
    /// <summary>Recalcula a quota ou prestação para o prazo restante informado.</summary>
    ReduzirPrestacao = 2
}

/// <summary>Contrato estimado, com parcelas mensais e CET anual decimal (0.12 = 12%).</summary>
public sealed record SimularFinanciamentoRequest
{
    /// <summary>Saldo devedor atual em reais, entre 0.01 e 1000000000, com no máximo duas casas decimais. Obrigatório.</summary>
    /// <example>200000.00</example>
    [Range(typeof(decimal), "0.01", "1000000000", ParseLimitsInInvariantCulture = true), ValorMonetario]
    public required decimal SaldoDevedor { get; init; }

    /// <summary>Quantidade de parcelas mensais restantes, de 1 a 480. Obrigatório.</summary>
    /// <example>240</example>
    [Range(1, 480)]
    public required int ParcelasRestantes { get; init; }

    /// <summary>Custo efetivo total anual em fração decimal, entre 0 e 10. Informe 0.12 para 12% ao ano; zero indica ausência de juros. Obrigatório.</summary>
    /// <example>0.12</example>
    [Range(typeof(decimal), "0", "10", ParseLimitsInInvariantCulture = true)]
    public required decimal CetAnual { get; init; }

    /// <summary>Sistema do contrato: "SAC" ou "PRICE", enviado como texto. Obrigatório.</summary>
    /// <example>SAC</example>
    [EnumDataType(typeof(SistemaAmortizacao))]
    public required SistemaAmortizacao SistemaAmortizacao { get; init; }
}

/// <summary>Contrato e aporte imediato, aplicado antes dos juros da próxima parcela.</summary>
public sealed record SimularAmortizacaoRequest : IValidatableObject
{
    /// <summary>Saldo devedor antes do aporte, em reais, entre 0.01 e 1000000000, com no máximo duas casas decimais. Obrigatório.</summary>
    /// <example>200000.00</example>
    [Range(typeof(decimal), "0.01", "1000000000", ParseLimitsInInvariantCulture = true), ValorMonetario]
    public required decimal SaldoDevedor { get; init; }

    /// <summary>Quantidade de parcelas mensais restantes antes do aporte, de 1 a 480. Obrigatório.</summary>
    /// <example>240</example>
    [Range(1, 480)]
    public required int ParcelasRestantes { get; init; }

    /// <summary>CET anual em fração decimal, entre 0 e 10. Informe 0.12 para 12% ao ano. Obrigatório.</summary>
    /// <example>0.12</example>
    [Range(typeof(decimal), "0", "10", ParseLimitsInInvariantCulture = true)]
    public required decimal CetAnual { get; init; }

    /// <summary>Sistema do contrato: "SAC" ou "PRICE", enviado como texto. Obrigatório.</summary>
    /// <example>PRICE</example>
    [EnumDataType(typeof(SistemaAmortizacao))]
    public required SistemaAmortizacao SistemaAmortizacao { get; init; }

    /// <summary>Destino do aporte: "ReduzirPrazo" ou "ReduzirPrestacao", enviado como texto. Obrigatório.</summary>
    /// <example>ReduzirPrazo</example>
    [EnumDataType(typeof(ObjetivoAmortizacao))]
    public required ObjetivoAmortizacao ObjetivoAmortizacao { get; init; }

    /// <summary>Aporte imediato em reais, entre zero e o saldo devedor, limitado a 1000000000 e a duas casas decimais. Zero mantém o contrato; o saldo integral quita a dívida. Obrigatório.</summary>
    /// <example>20000.00</example>
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true), ValorMonetario]
    public required decimal ValorAmortizacao { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValorAmortizacao > SaldoDevedor)
            yield return new ValidationResult("A amortização não pode exceder o saldo devedor.",
                [nameof(ValorAmortizacao)]);
    }
}

/// <summary>Valores monetários devem ter no máximo duas casas decimais.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ValorMonetarioAttribute : ValidationAttribute
{
    public ValorMonetarioAttribute() : base("O valor monetário deve ter no máximo duas casas decimais.") { }
    public override bool IsValid(object? value) => value is decimal amount && decimal.Round(amount, 2) == amount;
}

/// <summary>Evolução mensal; saldo final = saldo inicial - amortização.</summary>
/// <remarks>Valores monetários em reais, arredondados para duas casas decimais.</remarks>
/// <param name="Numero">Número sequencial da parcela no cronograma simulado, iniciando em 1.</param>
/// <param name="SaldoInicial">Saldo devedor antes do pagamento da parcela, em reais.</param>
/// <param name="Juros">Juros do mês sobre o saldo devedor, em reais.</param>
/// <param name="Amortizacao">Parte do pagamento que reduz o principal, em reais: saldo inicial menos saldo final.</param>
/// <param name="Prestacao">Pagamento mensal em reais: amortização mais juros.</param>
/// <param name="SaldoFinal">Saldo devedor após o pagamento, em reais; zero indica quitação.</param>
public sealed record ParcelaResponse(int Numero, decimal SaldoInicial, decimal Juros,
    decimal Amortizacao, decimal Prestacao, decimal SaldoFinal);

/// <summary>Cronograma estimado e totais, sem seguros, tarifas ou indexadores separados.</summary>
/// <remarks>Valores monetários em reais. Taxas são frações decimais, não percentuais inteiros. Totais somam os valores exibidos nas parcelas.</remarks>
/// <param name="SistemaAmortizacao">Sistema utilizado no cronograma: "SAC" ou "PRICE".</param>
/// <param name="SaldoDevedorInicial">Saldo devedor em reais no início deste cronograma, já descontado o aporte quando aplicável.</param>
/// <param name="CetAnual">CET anual informado, em fração decimal; 0.12 corresponde a 12% ao ano.</param>
/// <param name="TaxaMensalEfetiva">Taxa mensal em fração decimal, calculada por (1 + CET anual)^(1/12) - 1, sem arredondamento monetário.</param>
/// <param name="QuantidadeParcelas">Quantidade efetiva de parcelas até a quitação; pode ser zero após aporte integral.</param>
/// <param name="PrimeiraPrestacao">Valor em reais da primeira prestação; zero quando não há parcelas.</param>
/// <param name="UltimaPrestacao">Valor em reais da última prestação, ajustada para quitar o saldo; zero quando não há parcelas.</param>
/// <param name="TotalAmortizado">Soma da amortização das parcelas, em reais; não inclui o aporte extraordinário.</param>
/// <param name="TotalJuros">Soma dos juros das parcelas, em reais.</param>
/// <param name="TotalPrestacoes">Soma das prestações, em reais: total amortizado mais total de juros.</param>
/// <param name="Parcelas">Cronograma mensal ordenado pelo número da parcela; lista vazia quando o saldo já está quitado.</param>
public sealed record FinanciamentoResponse(SistemaAmortizacao SistemaAmortizacao,
    decimal SaldoDevedorInicial, decimal CetAnual, decimal TaxaMensalEfetiva,
    int QuantidadeParcelas, decimal PrimeiraPrestacao, decimal UltimaPrestacao,
    decimal TotalAmortizado, decimal TotalJuros, decimal TotalPrestacoes,
    IReadOnlyList<ParcelaResponse> Parcelas);

/// <summary>Comparação entre o contrato atual e o contrato após o aporte.</summary>
/// <param name="ObjetivoAmortizacao">Estratégia aplicada: "ReduzirPrazo" ou "ReduzirPrestacao".</param>
/// <param name="ValorAmortizacao">Aporte extraordinário aplicado imediatamente, em reais.</param>
/// <param name="SimulacaoAtual">Cronograma e totais do contrato sem aporte extraordinário.</param>
/// <param name="SimulacaoAposAmortizacao">Cronograma e totais após descontar o aporte do saldo devedor.</param>
/// <param name="ParcelasReduzidas">Diferença entre a quantidade de parcelas do contrato atual e a quantidade após o aporte.</param>
/// <param name="EconomiaJuros">Juros do contrato atual menos juros do contrato após o aporte, em reais.</param>
/// <param name="TotalDesembolsoAposAmortizacao">Desembolso futuro total em reais: aporte extraordinário mais soma das prestações após o aporte.</param>
public sealed record AmortizacaoResponse(ObjetivoAmortizacao ObjetivoAmortizacao,
    decimal ValorAmortizacao, FinanciamentoResponse SimulacaoAtual,
    FinanciamentoResponse SimulacaoAposAmortizacao, int ParcelasReduzidas,
    decimal EconomiaJuros, decimal TotalDesembolsoAposAmortizacao);