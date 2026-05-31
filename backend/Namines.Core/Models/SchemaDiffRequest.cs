namespace Namines.Core.Models;

public class SchemaDiffRequest
{
    public DatabaseSchema OldSchema { get; set; } = new();
    public DatabaseSchema NewSchema { get; set; } = new();
}
