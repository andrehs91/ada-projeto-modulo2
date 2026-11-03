using System.ComponentModel.DataAnnotations;
using api_simulador_habitacional.Contracts;
using api_simulador_habitacional.Services;
using Microsoft.AspNetCore.Mvc;

namespace api_simulador_habitacional.Controllers;

/// <summary>Simulações de financiamento habitacional e amortização extraordinária.</summary>
/// <remarks>Operações sem persistência. Valores monetários são expressos em reais e taxas em fração decimal.</remarks>
[ApiController]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/simulacoes")]
public sealed class SimulacoesController(ISimulacaoService service) : ControllerBase
{
    /// <summary>Estima a evolução mensal de um financiamento SAC ou PRICE.</summary>
    /// <remarks>
    /// Calcula o cronograma a partir do saldo atual e das parcelas restantes.
    /// O CET anual é convertido em taxa mensal efetiva pela fórmula (1 + CET anual)^(1/12) - 1.
    /// Informe 0.12 para 12% ao ano e o sistema como "SAC" ou "PRICE".
    /// Os valores exibidos são arredondados para centavos; seguros, tarifas e indexadores não são calculados separadamente.
    /// Exemplo de corpo JSON:
    /// { "saldoDevedor": 200000, "parcelasRestantes": 240, "cetAnual": 0.12, "sistemaAmortizacao": "SAC" }
    /// </remarks>
    /// <param name="request">Saldo devedor, prazo restante, CET anual e sistema de amortização; todos os campos são obrigatórios.</param>
    /// <param name="cancellationToken">Token associado ao cancelamento da requisição HTTP.</param>
    /// <returns>Cronograma mensal e totais estimados do financiamento.</returns>
    /// <response code="200">Simulação calculada com cronograma completo e totais.</response>
    /// <response code="400">JSON inválido, campo obrigatório ausente ou valor fora dos limites. Os erros são informados em errors.</response>
    [HttpPost("financiamento")]
    [Consumes("application/json")]
    [ProducesResponseType<FinanciamentoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<FinanciamentoResponse> SimularFinanciamento(
        [FromBody, Required] SimularFinanciamentoRequest request, CancellationToken cancellationToken)
        => Ok(service.SimularFinanciamento(request, cancellationToken));

    /// <summary>Compara o contrato atual com um aporte para reduzir prazo ou prestação.</summary>
    /// <remarks>
    /// O aporte é aplicado imediatamente ao saldo, antes dos juros da próxima parcela, e não pode exceder o saldo devedor.
    /// "ReduzirPrazo" preserva a quota de amortização do SAC ou a prestação de referência do PRICE.
    /// "ReduzirPrestacao" recalcula o contrato para o prazo restante informado, sujeito à quitação antecipada em centavos.
    /// Aporte zero mantém o contrato; aporte igual ao saldo quita a dívida e gera um cronograma sem parcelas futuras.
    /// Exemplo de corpo JSON:
    /// { "saldoDevedor": 200000, "parcelasRestantes": 240, "cetAnual": 0.12, "sistemaAmortizacao": "PRICE", "objetivoAmortizacao": "ReduzirPrazo", "valorAmortizacao": 20000 }
    /// </remarks>
    /// <param name="request">Dados do contrato, objetivo e valor do aporte; todos os campos são obrigatórios.</param>
    /// <param name="cancellationToken">Token associado ao cancelamento da requisição HTTP.</param>
    /// <returns>Simulações antes e depois do aporte, redução do prazo, economia de juros e desembolso futuro total.</returns>
    /// <response code="200">Comparação calculada entre o contrato atual e o contrato após o aporte.</response>
    /// <response code="400">JSON inválido, campo ausente, valor fora dos limites ou aporte superior ao saldo. Os erros são informados em errors.</response>
    [HttpPost("amortizacao")]
    [Consumes("application/json")]
    [ProducesResponseType<AmortizacaoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<AmortizacaoResponse> SimularAmortizacao(
        [FromBody, Required] SimularAmortizacaoRequest request, CancellationToken cancellationToken)
        => Ok(service.SimularAmortizacao(request, cancellationToken));
}