# Simulações habitacionais

## Endpoints

- `POST /api/simulacoes/financiamento`: saldoDevedor, parcelasRestantes, cetAnual,
  sistemaAmortizacao (`SAC` ou `PRICE`).
- `POST /api/simulacoes/amortizacao`: mesmos campos, objetivoAmortizacao
  (`ReduzirPrazo` ou `ReduzirPrestacao`) e valorAmortizacao.

Exemplos executáveis estão em `api-simulador-habitacional/api-simulador-habitacional.http`.
Não há persistência: ambas as operações retornam 200 com a simulação, não criam contratos.
Entradas inválidas retornam 400 com ValidationProblemDetails. Todos os campos são
obrigatórios; enums são strings. Valores monetários têm até duas casas decimais.
Saldo: R$ 0,01 a R$ 1 bilhão; prazo: 1 a 480 meses; CET anual: 0 a 10
(0% a 1000%, limite técnico). Aporte: zero a saldo total, inclusive quitação.

## Hipóteses e fórmulas

O CET é anual e decimal: **0.12 significa 12% ao ano**, não 0,12%.
A taxa mensal efetiva é `i = (1 + CET)^(1/12) - 1`, e não CET/12.
As parcelas vencem ao final de cada mês; a amortização extraordinária ocorre agora,
antes de qualquer juro da próxima parcela. Não há juros diários, atrasos,
indexadores TR/IPCA, seguros ou tarifas calculados separadamente.

**Atenção:** CET engloba juros, tributos, tarifas e seguros. Usá-lo como taxa de
juros uniforme é uma aproximação didática solicitada para esta API. O CET sozinho
não permite reproduzir o boleto, o saldo corrigido ou a economia exata de um banco.
O total denominado `totalJuros` é o custo estimado por essa taxa substituta.

Para saldo P, prazo n e taxa mensal i:
- SAC: amortização A = P/n; juros J = saldo inicial do mês * i;
  prestação = A + J. As prestações são decrescentes.
- PRICE: prestação T = P*i / (1 - (1+i)^(-n)); juros J = saldo * i;
  amortização = T - J. Para i=0, T=P/n.

Após o aporte E, o saldo passa a P-E:
- Reduzir prestação: manter n e recalcular SAC ou PRICE para o novo saldo.
- Reduzir prazo em SAC: manter a quota de amortização P/n (não a prestação total).
  Os juros diminuem imediatamente e o cronograma termina antes.
- Reduzir prazo em PRICE: manter a prestação teórica original e pagar até quitar,
  com a última parcela menor quando necessário.
- Quitação total: cronograma vazio e totais futuros zero.

Os cálculos internos usam decimal sem arredondamento mensal. Na saída, saldos e
juros são arredondados a centavos, com ponto médio afastado de zero; a amortização
é a diferença dos saldos exibidos e a prestação é amortização + juros. Os totais
são somas das parcelas exibidas, garantindo consistência contábil da resposta.
Na PRICE pode haver diferença de um centavo entre prestações exibidas. Não é uma
reprodução das regras de arredondamento de uma instituição financeira.

## Respostas e reuso

Financiamento retorna taxa mensal, saldo inicial, quantidade de parcelas,
primeira/última prestação, totais e lista mensal com saldo inicial/final,
amortização, juros e prestação. A numeração é relativa ao prazo restante.

Amortização retorna `simulacaoAtual`, `simulacaoAposAmortizacao`, parcelas reduzidas,
economia de juros e desembolso futuro total **incluindo o aporte imediato**.
`SimulacoesController` apenas recebe/valida HTTP e chama `ISimulacaoService`.
`SimulacaoService` é independente de HTTP e reutiliza o mesmo motor de cronograma
nos dois cenários. Não chama outro endpoint por HTTP.

## Fontes consultadas

- [Banco Central: metodologia de prestações fixas](https://www3.bcb.gov.br/CALCIDADAO/publico/exibirMetodologiaFinanciamentoPrestacoesFixas.do?method=exibirMetodologiaFinanciamentoPrestacoesFixas)
  — valor presente, capitalização mensal e fórmula PRICE.
- [Banco Central: Resolução CMN 4.881/2020, arts. 3–5](https://www.bcb.gov.br/estabilidadefinanceira/exibenormativo?tipo=Resolu%C3%A7%C3%A3o%20CMN&numero=4881)
  — CET anual e componentes de custo.
- [CAIXA: serviços para o contrato](https://www.caixa.gov.br/voce/habitacao/servicos/Paginas/default.aspx)
  — amortização com redução de prazo ou prestação.
- [CAIXA: perguntas frequentes](https://www.caixa.gov.br/voce/habitacao/financiamento/perguntas-frequentes/Paginas/default.aspx)
  — SAC/PRICE e distinção entre encargos principais e acessórios.
- [SAC: fórmulas e exemplo numérico](https://pt.wikipedia.org/wiki/Sistema_de_amortiza%C3%A7%C3%A3o_constante)

As escolhas de recálculo acima são hipóteses do simulador. As fontes não estabelecem
um algoritmo universal para todas as instituições; contratos bancários podem diferir.