namespace OEESentinel.Domain.Entities;

public class Machine
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    // Tempo teórico ideal para produzir 1 unidade (em segundos)
    public double IdealCycleTimeSeconds { get; private set; }

    private Machine() { } // Para o EF Core

    public Machine(string code, string name, double idealCycleTimeSeconds)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("O código da máquina é obrigatório.", nameof(code));

        if (idealCycleTimeSeconds <= 0)
            throw new ArgumentException("O tempo de ciclo ideal deve ser superior a zero.", nameof(idealCycleTimeSeconds));

        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IdealCycleTimeSeconds = idealCycleTimeSeconds;
    }
}