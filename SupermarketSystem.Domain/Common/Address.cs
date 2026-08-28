namespace SupermarketSystem.Domain.Common;

/// <summary>
/// Value object (EF Core owned type). No independent identity or lifecycle —
/// used on Branch/Supplier (and optionally Customer). Per Architecture
/// Review §6: this is the one place a formal value object earns its keep;
/// Money was deliberately NOT introduced as a wrapper type (single-currency
/// assumption), so plain decimal columns are used for money everywhere else.
/// </summary>
public sealed class Address
{
    public string? Street { get; private set; }
    public string? City { get; private set; }
    public string? PostalCode { get; private set; }
    public string? Country { get; private set; }

    private Address() { } // EF Core

    public Address(string? street, string? city, string? postalCode, string? country)
    {
        Street = street;
        City = city;
        PostalCode = postalCode;
        Country = country;
    }
}
