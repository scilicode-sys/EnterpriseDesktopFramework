using Framework.Core.Time;

namespace Framework.Tests;

/// <summary>Horloge de test : on avance le temps, on ne dort jamais.</summary>
internal sealed class FakeHorloge : IHorloge
{
    public DateTime Maintenant { get; set; } = new(2026, 1, 15, 10, 0, 0);
}
