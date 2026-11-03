using System.ComponentModel.DataAnnotations;
using api_simulador_habitacional.Contracts;

namespace api_simulador_habitacional.Services;

/// <summary>Modelo mensal simplificado, sem estado e independente de HTTP.</summary>
public sealed class SimulacaoService : ISimulacaoService
{
    public FinanciamentoResponse SimularFinanciamento(SimularFinanciamentoRequest request,
        CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        return GerarCronograma(request, request.SaldoDevedor, preservarQuota: false, cancellationToken);
    }

    public AmortizacaoResponse SimularAmortizacao(SimularAmortizacaoRequest request,
        CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        var contrato = new SimularFinanciamentoRequest
        {
            SaldoDevedor = request.SaldoDevedor,
            ParcelasRestantes = request.ParcelasRestantes,
            CetAnual = request.CetAnual,
            SistemaAmortizacao = request.SistemaAmortizacao
        };
        // Reutiliza a mesma simulação disponível no endpoint de financiamento.
        var atual = SimularFinanciamento(contrato, cancellationToken);
        var apos = GerarCronograma(contrato, request.SaldoDevedor - request.ValorAmortizacao,
            request.ObjetivoAmortizacao == ObjetivoAmortizacao.ReduzirPrazo, cancellationToken);
        return new(request.ObjetivoAmortizacao, request.ValorAmortizacao, atual, apos,
            atual.QuantidadeParcelas - apos.QuantidadeParcelas,
            atual.TotalJuros - apos.TotalJuros, request.ValorAmortizacao + apos.TotalPrestacoes);
    }

    private static FinanciamentoResponse GerarCronograma(SimularFinanciamentoRequest contrato,
        decimal saldo, bool preservarQuota, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inicial = saldo;
        var taxa = TaxaMensal(contrato.CetAnual);
        var principalReferencia = preservarQuota ? contrato.SaldoDevedor : saldo;
        var quota = contrato.SistemaAmortizacao == SistemaAmortizacao.SAC
            ? principalReferencia / contrato.ParcelasRestantes
            : PrestacaoPrice(principalReferencia, taxa, contrato.ParcelasRestantes);
        var parcelas = new List<ParcelaResponse>(contrato.ParcelasRestantes);

        for (var numero = 1; numero <= contrato.ParcelasRestantes && saldo > 0; numero++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var juros = saldo * taxa;
            var amortizacao = contrato.SistemaAmortizacao == SistemaAmortizacao.SAC ? quota : quota - juros;
            // Em PRICE com taxa/prazo extremos, a quota pode se igualar aos juros
            // na precisão decimal. Evita capitalizar um resíduo numérico negativo.
            amortizacao = Math.Max(0m, amortizacao);
            // Ajusta a última parcela para quitar resíduos de precisão decimal.
            amortizacao = numero == contrato.ParcelasRestantes ? saldo : Math.Min(saldo, amortizacao);
            var saldoFinal = Math.Max(0, saldo - amortizacao);
            var saldoInicialExibido = Dinheiro(saldo);
            var saldoFinalExibido = Dinheiro(saldoFinal);
            var amortizacaoExibida = saldoInicialExibido - saldoFinalExibido;
            var jurosExibidos = Dinheiro(juros);
            parcelas.Add(new(numero, saldoInicialExibido, jurosExibidos, amortizacaoExibida,
                amortizacaoExibida + jurosExibidos, saldoFinalExibido));
            // Após quitação em centavos, não cria parcelas adicionais de valor zero.
            saldo = saldoFinalExibido == 0 ? 0 : saldoFinal;
        }

        return new(contrato.SistemaAmortizacao, inicial, contrato.CetAnual, taxa,
            parcelas.Count, parcelas.FirstOrDefault()?.Prestacao ?? 0,
            parcelas.LastOrDefault()?.Prestacao ?? 0,
            parcelas.Sum(p => p.Amortizacao), parcelas.Sum(p => p.Juros),
            parcelas.Sum(p => p.Prestacao), parcelas.AsReadOnly());
    }

    // Raiz décima segunda por Newton em decimal: evita perda de precisão para CET muito pequeno.
    private static decimal TaxaMensal(decimal cetAnual)
    {
        if (cetAnual == 0) return 0;
        var fator = 1m;
        for (var i = 0; i < 100; i++)
        {
            var potencia = 1m;
            for (var j = 0; j < 11; j++) potencia *= fator;
            var proximo = (11 * fator + (1 + cetAnual) / potencia) / 12;
            if (Math.Abs(proximo - fator) < 0.000000000000000000000001m) return proximo - 1;
            fator = proximo;
        }
        return fator - 1;
    }

    // PV = PMT * soma dos fatores de desconto. Equivale à fórmula PRICE,
    // incluindo taxa zero, sem subtração de números quase iguais.
    private static decimal PrestacaoPrice(decimal saldo, decimal taxa, int prazo)
    {
        var desconto = 1m;
        var soma = 0m;
        for (var i = 0; i < prazo; i++)
        {
            desconto /= 1 + taxa;
            soma += desconto;
        }
        return saldo / soma;
    }

    private static decimal Dinheiro(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
}