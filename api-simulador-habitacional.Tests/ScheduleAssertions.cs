using api_simulador_habitacional.Contracts;
using Xunit;

namespace ApiSimuladorHabitacional.Tests;

internal static class ScheduleAssertions
{
    internal static void Valid(FinanciamentoResponse result, decimal principal, int originalTerm)
    {
        Assert.Equal(principal, result.SaldoDevedorInicial);
        Assert.Equal(result.Parcelas.Count, result.QuantidadeParcelas);
        Assert.InRange(result.QuantidadeParcelas, principal == 0 ? 0 : 1, originalTerm);
        Assert.Equal(principal, result.TotalAmortizado);
        Assert.Equal(result.Parcelas.Sum(p => p.Amortizacao), result.TotalAmortizado);
        Assert.Equal(result.Parcelas.Sum(p => p.Juros), result.TotalJuros);
        Assert.Equal(result.Parcelas.Sum(p => p.Prestacao), result.TotalPrestacoes);
        Assert.Equal(result.TotalAmortizado + result.TotalJuros, result.TotalPrestacoes);
        Assert.Equal(result.Parcelas.FirstOrDefault()?.Prestacao ?? 0, result.PrimeiraPrestacao);
        Assert.Equal(result.Parcelas.LastOrDefault()?.Prestacao ?? 0, result.UltimaPrestacao);
        Assert.True(result.TaxaMensalEfetiva >= 0);

        var balance = principal;
        for (var i = 0; i < result.Parcelas.Count; i++)
        {
            var installment = result.Parcelas[i];
            Assert.Equal(i + 1, installment.Numero);
            Assert.Equal(balance, installment.SaldoInicial);
            Assert.InRange(installment.SaldoFinal, 0m, balance);
            Assert.InRange(installment.Amortizacao, 0m, balance);
            Assert.True(installment.Juros >= 0);
            Assert.Equal(installment.SaldoInicial - installment.Amortizacao, installment.SaldoFinal);
            Assert.Equal(installment.Amortizacao + installment.Juros, installment.Prestacao);
            foreach (var amount in new[] { installment.SaldoInicial, installment.SaldoFinal,
                         installment.Amortizacao, installment.Juros, installment.Prestacao })
                Assert.Equal(decimal.Round(amount, 2), amount);
            balance = installment.SaldoFinal;
        }
        Assert.Equal(0m, balance);
        if (principal == 0)
            Assert.Empty(result.Parcelas);
        else
            Assert.Equal(0m, result.Parcelas[^1].SaldoFinal);
    }

    internal static void Comparison(AmortizacaoResponse result, decimal principal, int term)
    {
        Valid(result.SimulacaoAtual, principal, term);
        Valid(result.SimulacaoAposAmortizacao, principal - result.ValorAmortizacao, term);
        Assert.Equal(result.SimulacaoAtual.QuantidadeParcelas -
                     result.SimulacaoAposAmortizacao.QuantidadeParcelas, result.ParcelasReduzidas);
        Assert.Equal(result.SimulacaoAtual.TotalJuros - result.SimulacaoAposAmortizacao.TotalJuros,
            result.EconomiaJuros);
        Assert.True(result.EconomiaJuros >= 0);
        Assert.Equal(result.ValorAmortizacao + result.SimulacaoAposAmortizacao.TotalPrestacoes,
            result.TotalDesembolsoAposAmortizacao);
        Assert.True(result.TotalDesembolsoAposAmortizacao <= result.SimulacaoAtual.TotalPrestacoes);
    }
}