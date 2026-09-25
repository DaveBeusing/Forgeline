namespace ForgeLine.Economy;

public readonly record struct ResourceExtractionMetrics(
    int DepositCount,
    int DepletedDepositCount,
    int ExtractorCount,
    int ActiveExtractorCount,
    double LastTickExtractedQuantity,
    double LastTickExtractionRatePerSecond,
    double TotalExtractedQuantity);
