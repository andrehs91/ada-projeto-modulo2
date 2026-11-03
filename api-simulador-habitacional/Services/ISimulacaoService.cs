using api_simulador_habitacional.Contracts;

namespace api_simulador_habitacional.Services;

public interface ISimulacaoService
{
    FinanciamentoResponse SimularFinanciamento(SimularFinanciamentoRequest request,
        CancellationToken cancellationToken = default);
    AmortizacaoResponse SimularAmortizacao(SimularAmortizacaoRequest request,
        CancellationToken cancellationToken = default);
}