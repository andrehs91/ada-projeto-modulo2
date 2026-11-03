using System.ComponentModel.DataAnnotations;
using api_simulador_habitacional.Contracts;
using api_simulador_habitacional.Services;
using Xunit;

namespace ApiSimuladorHabitacional.Tests;

[Trait("Category", "Unit")]
public sealed class SimulacaoServiceTests
{
    private readonly SimulacaoService service = new();
    // Decimal exponentiation is independent of the service's Newton root implementation.
    private static readonly decimal CetThreePercent = Enumerable.Repeat(1.03m, 12)
        .Aggregate(1m, (product, factor) => product * factor) - 1m;

    private static SimularFinanciamentoRequest Financing(SistemaAmortizacao system = SistemaAmortizacao.SAC)
        => new() { SaldoDevedor = 1000m, ParcelasRestantes = 4,
            CetAnual = CetThreePercent, SistemaAmortizacao = system };

    private static SimularAmortizacaoRequest Amortization(SistemaAmortizacao system,
        ObjetivoAmortizacao goal, decimal contribution = 300m)
        => new() { SaldoDevedor = 1000m, ParcelasRestantes = 4, CetAnual = CetThreePercent,
            SistemaAmortizacao = system, ObjetivoAmortizacao = goal, ValorAmortizacao = contribution };

    [Fact]
    public void Sac_KnownScheduleAtThreePercentMonthly()
    {
        var result = service.SimularFinanciamento(Financing());
        Assert.InRange(result.TaxaMensalEfetiva, .03m - 1e-22m, .03m + 1e-22m);
        Assert.Equal(new[]
        {
            new ParcelaResponse(1, 1000m, 30m, 250m, 280m, 750m),
            new ParcelaResponse(2, 750m, 22.50m, 250m, 272.50m, 500m),
            new ParcelaResponse(3, 500m, 15m, 250m, 265m, 250m),
            new ParcelaResponse(4, 250m, 7.50m, 250m, 257.50m, 0m)
        }, result.Parcelas);
        Assert.Equal(75m, result.TotalJuros);
        Assert.Equal(1075m, result.TotalPrestacoes);
        ScheduleAssertions.Valid(result, 1000m, 4);
    }

    [Fact]
    public void Price_MatchesIndependentDoubleAnnuityCalculation()
    {
        const double rate = .03;
        const int term = 4;
        var payment = 1000 * rate / (1 - Math.Pow(1 + rate, -term));
        var balance = 1000d;
        var result = service.SimularFinanciamento(Financing(SistemaAmortizacao.PRICE));
        foreach (var installment in result.Parcelas)
        {
            var interest = balance * rate;
            var principal = installment.Numero == term ? balance : payment - interest;
            Near(balance, installment.SaldoInicial);
            Near(interest, installment.Juros);
            Near(principal, installment.Amortizacao);
            Near(principal + interest, installment.Prestacao);
            balance -= principal;
            Near(balance, installment.SaldoFinal);
        }
        Near(payment * term - 1000, result.TotalJuros);
        Near(payment * term, result.TotalPrestacoes);
        ScheduleAssertions.Valid(result, 1000m, 4);
    }

    private static void Near(double expected, decimal actual)
        => Assert.InRange(actual, (decimal)expected - .01m, (decimal)expected + .01m);

    [Theory]
    [InlineData(SistemaAmortizacao.SAC)]
    [InlineData(SistemaAmortizacao.PRICE)]
    public void ZeroRate_HasEqualPrincipalInstallmentsAndNoInterest(SistemaAmortizacao system)
    {
        var result = service.SimularFinanciamento(Financing(system) with { CetAnual = 0 });
        Assert.Equal(0m, result.TaxaMensalEfetiva);
        Assert.Equal(0m, result.TotalJuros);
        Assert.All(result.Parcelas, p => { Assert.Equal(250m, p.Prestacao); Assert.Equal(250m, p.Amortizacao); });
        ScheduleAssertions.Valid(result, 1000m, 4);
    }

    [Theory]
    [InlineData(SistemaAmortizacao.SAC)]
    [InlineData(SistemaAmortizacao.PRICE)]
    public void ReducePayment_PreservesTermAndLowersEveryPayment(SistemaAmortizacao system)
    {
        var result = service.SimularAmortizacao(Amortization(system, ObjetivoAmortizacao.ReduzirPrestacao));
        Assert.Equal(4, result.SimulacaoAposAmortizacao.QuantidadeParcelas);
        Assert.Equal(0, result.ParcelasReduzidas);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(result.SimulacaoAposAmortizacao.Parcelas[i].Prestacao < result.SimulacaoAtual.Parcelas[i].Prestacao);
            Assert.True(result.SimulacaoAposAmortizacao.Parcelas[i].Juros < result.SimulacaoAtual.Parcelas[i].Juros);
        }
        ScheduleAssertions.Comparison(result, 1000m, 4);
    }

    [Fact]
    public void Sac_ReduceTerm_PreservesOriginalQuotaWithPartialFinalInstallment()
    {
        var result = service.SimularAmortizacao(Amortization(SistemaAmortizacao.SAC, ObjetivoAmortizacao.ReduzirPrazo));
        Assert.Equal(new[] { 250m, 250m, 200m }, result.SimulacaoAposAmortizacao.Parcelas.Select(p => p.Amortizacao));
        Assert.Equal(new[] { 271m, 263.50m, 206m }, result.SimulacaoAposAmortizacao.Parcelas.Select(p => p.Prestacao));
        Assert.Equal(1, result.ParcelasReduzidas);
        ScheduleAssertions.Comparison(result, 1000m, 4);
    }

    [Fact]
    public void Price_ReduceTerm_PreservesPaymentWithinOneCentUntilPayoff()
    {
        var result = service.SimularAmortizacao(Amortization(SistemaAmortizacao.PRICE, ObjetivoAmortizacao.ReduzirPrazo));
        Assert.Equal(3, result.SimulacaoAposAmortizacao.QuantidadeParcelas);
        Assert.Equal(1, result.ParcelasReduzidas);
        var original = result.SimulacaoAtual.PrimeiraPrestacao;
        Assert.All(result.SimulacaoAposAmortizacao.Parcelas.SkipLast(1),
            p => Assert.InRange(p.Prestacao, original - .01m, original + .01m));
        Assert.InRange(result.SimulacaoAposAmortizacao.UltimaPrestacao, .01m, original);
        ScheduleAssertions.Comparison(result, 1000m, 4);
    }

    [Theory]
    [InlineData(SistemaAmortizacao.SAC, ObjetivoAmortizacao.ReduzirPrazo)]
    [InlineData(SistemaAmortizacao.PRICE, ObjetivoAmortizacao.ReduzirPrazo)]
    [InlineData(SistemaAmortizacao.SAC, ObjetivoAmortizacao.ReduzirPrestacao)]
    [InlineData(SistemaAmortizacao.PRICE, ObjetivoAmortizacao.ReduzirPrestacao)]
    public void FullPayoff_HasEmptyScheduleAndSavesAllInterest(SistemaAmortizacao system, ObjetivoAmortizacao goal)
    {
        var result = service.SimularAmortizacao(Amortization(system, goal, 1000m));
        Assert.Equal(4, result.ParcelasReduzidas);
        Assert.Equal(result.SimulacaoAtual.TotalJuros, result.EconomiaJuros);
        Assert.Equal(1000m, result.TotalDesembolsoAposAmortizacao);
        Assert.Equal(0m, result.SimulacaoAposAmortizacao.TotalJuros);
        ScheduleAssertions.Comparison(result, 1000m, 4);
    }

    [Theory]
    [InlineData(SistemaAmortizacao.SAC, ObjetivoAmortizacao.ReduzirPrazo)]
    [InlineData(SistemaAmortizacao.PRICE, ObjetivoAmortizacao.ReduzirPrazo)]
    [InlineData(SistemaAmortizacao.SAC, ObjetivoAmortizacao.ReduzirPrestacao)]
    [InlineData(SistemaAmortizacao.PRICE, ObjetivoAmortizacao.ReduzirPrestacao)]
    public void ZeroContribution_LeavesScheduleUnchanged(SistemaAmortizacao system, ObjetivoAmortizacao goal)
    {
        var result = service.SimularAmortizacao(Amortization(system, goal, 0));
        Assert.Equal(result.SimulacaoAtual.Parcelas, result.SimulacaoAposAmortizacao.Parcelas);
        Assert.Equal(0, result.ParcelasReduzidas);
        Assert.Equal(0m, result.EconomiaJuros);
        ScheduleAssertions.Comparison(result, 1000m, 4);
    }

    public static IEnumerable<object[]> BoundaryCases()
    {
        foreach (var system in Enum.GetValues<SistemaAmortizacao>())
        foreach (var principal in new[] { .01m, 1000.01m, 1_000_000_000m })
        foreach (var term in new[] { 1, 4, 480 })
        foreach (var cet in new[] { 0m, .000000000000000001m, 10m })
            yield return new object[] { system, principal, term, cet };
    }

    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public void BoundaryValues_PreserveScheduleInvariants(SistemaAmortizacao system, decimal principal, int term, decimal cet)
    {
        var request = Financing(system) with { SaldoDevedor = principal, ParcelasRestantes = term, CetAnual = cet };
        var result = service.SimularFinanciamento(request);
        Assert.Equal(system, result.SistemaAmortizacao);
        Assert.Equal(cet, result.CetAnual);
        if (cet > 0)
            Assert.True(result.TaxaMensalEfetiva > 0);
        ScheduleAssertions.Valid(result, principal, term);
        foreach (var goal in Enum.GetValues<ObjetivoAmortizacao>())
        {
            var aporte = decimal.Round(principal * .3m, 2);
            var comparison = service.SimularAmortizacao(new SimularAmortizacaoRequest
            {
                SaldoDevedor = principal, ParcelasRestantes = term, CetAnual = cet,
                SistemaAmortizacao = system, ObjetivoAmortizacao = goal, ValorAmortizacao = aporte
            });
            ScheduleAssertions.Comparison(comparison, principal, term);
        }
    }

    [Theory]
    [InlineData(SistemaAmortizacao.SAC)]
    [InlineData(SistemaAmortizacao.PRICE)]
    public void TinyCet_IsNotLostToDoublePrecision(SistemaAmortizacao system)
    {
        const decimal cet = .000000000000000001m;
        var result = service.SimularFinanciamento(Financing(system) with { CetAnual = cet });
        Assert.InRange(result.TaxaMensalEfetiva, cet / 12m * .999999m, cet / 12m * 1.000001m);
        Assert.Equal(0m, result.TotalJuros);
        ScheduleAssertions.Valid(result, 1000m, 4);
    }

    public static IEnumerable<object[]> InvalidFinancing()
    {
        var valid = Financing();
        foreach (var amount in new[] { -1m, 0m, .001m, 1000.001m, 1_000_000_000.01m, decimal.MaxValue })
            yield return new object[] { valid with { SaldoDevedor = amount }, nameof(valid.SaldoDevedor) };
        foreach (var term in new[] { -1, 0, 481, int.MaxValue })
            yield return new object[] { valid with { ParcelasRestantes = term }, nameof(valid.ParcelasRestantes) };
        foreach (var cet in new[] { -.01m, 10.01m, decimal.MaxValue })
            yield return new object[] { valid with { CetAnual = cet }, nameof(valid.CetAnual) };
        foreach (var system in new[] { -1, 0, 3, int.MaxValue })
            yield return new object[] { valid with { SistemaAmortizacao = (SistemaAmortizacao)system }, nameof(valid.SistemaAmortizacao) };
    }

    [Theory]
    [MemberData(nameof(InvalidFinancing))]
    public void InvalidFinancing_ThrowsValidationExceptionWithMember(SimularFinanciamentoRequest request, string member)
    {
        var exception = Assert.Throws<ValidationException>(() => service.SimularFinanciamento(request));
        Assert.Contains(member, exception.ValidationResult!.MemberNames);
    }

    public static IEnumerable<object[]> InvalidAmortization()
    {
        var valid = Amortization(SistemaAmortizacao.SAC, ObjetivoAmortizacao.ReduzirPrazo);
        foreach (var row in InvalidFinancing())
        {
            var financing = (SimularFinanciamentoRequest)row[0];
            yield return new object[] { valid with { SaldoDevedor = financing.SaldoDevedor,
                ParcelasRestantes = financing.ParcelasRestantes, CetAnual = financing.CetAnual,
                SistemaAmortizacao = financing.SistemaAmortizacao }, row[1] };
        }
        foreach (var amount in new[] { -1m, .001m, 300.001m, 1000.01m, 1_000_000_000.01m, decimal.MaxValue })
            yield return new object[] { valid with { ValorAmortizacao = amount }, nameof(valid.ValorAmortizacao) };
        foreach (var goal in new[] { -1, 0, 3, int.MaxValue })
            yield return new object[] { valid with { ObjetivoAmortizacao = (ObjetivoAmortizacao)goal }, nameof(valid.ObjetivoAmortizacao) };
    }

    [Theory]
    [MemberData(nameof(InvalidAmortization))]
    public void InvalidAmortization_ThrowsValidationExceptionWithMember(SimularAmortizacaoRequest request, string member)
    {
        var exception = Assert.Throws<ValidationException>(() => service.SimularAmortizacao(request));
        Assert.Contains(member, exception.ValidationResult!.MemberNames);
    }

    [Theory]
    [InlineData(SistemaAmortizacao.SAC)]
    [InlineData(SistemaAmortizacao.PRICE)]
    public void AlreadyCancelledToken_CancelsBothOperationsIncludingFullPayoff(SistemaAmortizacao system)
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.SimularFinanciamento(Financing(system), source.Token));
        foreach (var goal in Enum.GetValues<ObjetivoAmortizacao>())
        foreach (var contribution in new[] { 0m, 300m, 1000m })
            Assert.Throws<OperationCanceledException>(() =>
                service.SimularAmortizacao(Amortization(system, goal, contribution), source.Token));
    }
}