using System.ComponentModel.DataAnnotations;
using System.Globalization;
using api_simulador_habitacional.Contracts;
using Xunit;

namespace ApiSimuladorHabitacional.Tests;

public sealed class SimulacaoValidationCultureTests
{
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void DecimalRanges_ValidateIndependentlyOfCurrentCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var financiamento = new SimularFinanciamentoRequest
            {
                SaldoDevedor = 0.01m,
                ParcelasRestantes = 1,
                CetAnual = 0.12m,
                SistemaAmortizacao = SistemaAmortizacao.SAC
            };
            var amortizacao = new SimularAmortizacaoRequest
            {
                SaldoDevedor = 0.01m,
                ParcelasRestantes = 1,
                CetAnual = 0.12m,
                SistemaAmortizacao = SistemaAmortizacao.PRICE,
                ObjetivoAmortizacao = ObjetivoAmortizacao.ReduzirPrazo,
                ValorAmortizacao = 0.01m
            };

            Validator.ValidateObject(financiamento, new ValidationContext(financiamento), true);
            Validator.ValidateObject(amortizacao, new ValidationContext(amortizacao), true);

            var invalidFinanciamento = financiamento with { SaldoDevedor = 0m };
            var invalidAmortizacao = amortizacao with { ValorAmortizacao = -0.01m };
            Assert.Throws<ValidationException>(() => Validator.ValidateObject(
                invalidFinanciamento, new ValidationContext(invalidFinanciamento), true));
            Assert.Throws<ValidationException>(() => Validator.ValidateObject(
                invalidAmortizacao, new ValidationContext(invalidAmortizacao), true));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}