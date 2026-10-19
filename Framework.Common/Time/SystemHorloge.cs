using Framework.Core.Time;

namespace Framework.Common.Time;

/// <summary>Horloge système : l'unique implémentation de production de IHorloge.</summary>
public sealed class SystemHorloge : IHorloge
{
    public DateTime Maintenant => DateTime.Now;
}
