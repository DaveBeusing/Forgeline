namespace ForgeLine.Economy;

public readonly record struct ResourceExtractionMetrics(
    int DepositCount,
    int DepletedDepositCount,
    int ExtractorCount,
    int ActiveExtractorCount,
    int BlockedExtractorCount,
    double LastTickExtractedQuantity,
    double LastTickExtractionRatePerSecond,
    double TotalExtractedQuantity);
