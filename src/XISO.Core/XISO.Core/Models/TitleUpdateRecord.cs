namespace XISO.Core.Models;

public sealed class TitleUpdateRecord
{
    public int Number { get; set; }

    public string PackageName { get; set; } = string.Empty;

    public OperationStatus Status { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}