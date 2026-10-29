namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Eén rekening in de kolommenbalans: de totalen debet en credit, en daaruit het saldo in de juiste kolommen.</summary>
public sealed record Kolommenbalansregel(Rekening Rekening, decimal Debet, decimal Credit)
{
    public decimal Saldo => Debet - Credit;

    public decimal SaldoDebet => Saldo > 0 ? Saldo : 0;

    public decimal SaldoCredit => Saldo < 0 ? -Saldo : 0;

    public decimal BalansDebet => Rekening.IsBalans ? SaldoDebet : 0;

    public decimal BalansCredit => Rekening.IsBalans ? SaldoCredit : 0;

    public decimal WinstEnVerliesDebet => Rekening.IsBalans ? 0 : SaldoDebet;

    public decimal WinstEnVerliesCredit => Rekening.IsBalans ? 0 : SaldoCredit;
}

/// <summary>
/// De kolommenbalans over een periode: per rekening de proefbalans (totaal debet en credit), de saldibalans, en het
/// saldo op de balans of in de winst-en-verliesrekening. Bij een balansrekening telt de beginstand mee in de
/// proefbalans; het resultaat van vóór de periode staat op het onverdeelde resultaat. Het resultaat van de periode
/// (<see cref="Resultaat"/>) sluit de balans- en de W&amp;V-kolommen.
/// </summary>
public sealed record Kolommenbalans(DateOnly Van, DateOnly Tot, IReadOnlyList<Kolommenbalansregel> Regels)
{
    public decimal TotaalDebet => Regels.Sum(r => r.Debet);

    public decimal TotaalCredit => Regels.Sum(r => r.Credit);

    public decimal SaldoDebet => Regels.Sum(r => r.SaldoDebet);

    public decimal SaldoCredit => Regels.Sum(r => r.SaldoCredit);

    public decimal BalansDebet => Regels.Sum(r => r.BalansDebet);

    public decimal BalansCredit => Regels.Sum(r => r.BalansCredit);

    public decimal WinstEnVerliesDebet => Regels.Sum(r => r.WinstEnVerliesDebet);

    public decimal WinstEnVerliesCredit => Regels.Sum(r => r.WinstEnVerliesCredit);

    /// <summary>Het resultaat van de periode: winst positief. Op de balans credit, in de W&amp;V debet (bij verlies omgekeerd).</summary>
    public decimal Resultaat => WinstEnVerliesCredit - WinstEnVerliesDebet;
}
