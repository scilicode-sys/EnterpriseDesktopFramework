using System.Net.Http;
using Framework.Core.Licensing;
using Microsoft.Extensions.Logging;

namespace Framework.Common.Licensing;

/// <summary>
/// Contrôle de révocation en ligne sur un feed REST compatible Firebase
/// Realtime Database : GET {baseUrl}/licences/{produit}-{hwid}.json.
/// Forme du document : { "status": "active" | "revoked" }.
/// Ne lève jamais : panne, timeout ou réponse inattendue rendent Unreachable,
/// et le poste continue de faire confiance à sa licence hors-ligne.
/// </summary>
public sealed class OnlineLicenseValidator
{
    private static readonly TimeSpan Delai = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public OnlineLicenseValidator(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<OnlineCheckResult> VérifierAsync(
        string baseUrl,
        string product,
        string hwid,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/licences/{Uri.EscapeDataString($"{product}-{hwid}")}.json";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Delai);

            using var reponse = await _http.GetAsync(url, timeoutCts.Token);

            if (!reponse.IsSuccessStatusCode)
            {
                // Un code HTTP d'erreur (404 compris : licence non déclarée) n'est
                // jamais une révocation — seule la réponse explicite compte.
                return OnlineCheckResult.Unreachable;
            }

            var corps = await reponse.Content.ReadAsStringAsync(timeoutCts.Token);

            if (corps.Contains("\"revoked\"", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Licence révoquée en ligne pour {Produit}/{Hwid}.", product, hwid);
                return OnlineCheckResult.Revoked;
            }

            return corps.Contains("\"active\"", StringComparison.OrdinalIgnoreCase)
                ? OnlineCheckResult.Active
                : OnlineCheckResult.Unreachable;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Contrôle de licence en ligne injoignable : licence hors-ligne conservée.");
            return OnlineCheckResult.Unreachable;
        }
    }
}
