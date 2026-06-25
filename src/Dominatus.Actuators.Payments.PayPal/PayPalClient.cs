using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.Actuators.Payments.PayPal;

internal interface IPayPalApiClient
{
    ValueTask<PayPalOrderDto> CreateOrderAsync(PayPalCreateOrderRequest request, string? requestId, CancellationToken cancellationToken);
    ValueTask<PayPalOrderDto> CaptureOrderAsync(string orderId, string? requestId, CancellationToken cancellationToken);
    ValueTask<PayPalOrderDto> AuthorizeOrderAsync(string orderId, string? requestId, CancellationToken cancellationToken);
    ValueTask<PayPalCaptureDto> CaptureAuthorizationAsync(string authorizationId, PayPalCaptureAuthorizationRequest request, string? requestId, CancellationToken cancellationToken);
    ValueTask<PayPalRefundDto> RefundCaptureAsync(string captureId, PayPalRefundRequest request, string? requestId, CancellationToken cancellationToken);
    ValueTask<PayPalOrderDto> GetOrderAsync(string orderId, CancellationToken cancellationToken);
    ValueTask<PayPalAuthorizationDto> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    ValueTask<PayPalCaptureDto> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
}

internal sealed class PayPalHttpApiClient : IPayPalApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly PayPalPaymentProviderOptions _options;
    private readonly HttpClient _http;
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public PayPalHttpApiClient(PayPalPaymentProviderOptions options, HttpClient? http = null)
    {
        _options = options;
        _http = http ?? new HttpClient();
    }

    private Uri BaseUrl => _options.BaseUrl ?? new Uri(_options.Environment == PayPalEnvironment.Live
        ? "https://api-m.paypal.com"
        : "https://api-m.sandbox.paypal.com");

    public ValueTask<PayPalOrderDto> CreateOrderAsync(PayPalCreateOrderRequest request, string? requestId, CancellationToken cancellationToken) =>
        SendAsync<PayPalOrderDto>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);

    public ValueTask<PayPalOrderDto> CaptureOrderAsync(string orderId, string? requestId, CancellationToken cancellationToken) =>
        SendAsync<PayPalOrderDto>(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture", new { }, requestId, cancellationToken);

    public ValueTask<PayPalOrderDto> AuthorizeOrderAsync(string orderId, string? requestId, CancellationToken cancellationToken) =>
        SendAsync<PayPalOrderDto>(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize", new { }, requestId, cancellationToken);

    public ValueTask<PayPalCaptureDto> CaptureAuthorizationAsync(string authorizationId, PayPalCaptureAuthorizationRequest request, string? requestId, CancellationToken cancellationToken) =>
        SendAsync<PayPalCaptureDto>(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", request, requestId, cancellationToken);

    public ValueTask<PayPalRefundDto> RefundCaptureAsync(string captureId, PayPalRefundRequest request, string? requestId, CancellationToken cancellationToken) =>
        SendAsync<PayPalRefundDto>(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", request, requestId, cancellationToken);

    public ValueTask<PayPalOrderDto> GetOrderAsync(string orderId, CancellationToken cancellationToken) =>
        SendAsync<PayPalOrderDto>(HttpMethod.Get, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}", null, null, cancellationToken);

    public ValueTask<PayPalAuthorizationDto> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        SendAsync<PayPalAuthorizationDto>(HttpMethod.Get, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);

    public ValueTask<PayPalCaptureDto> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        SendAsync<PayPalCaptureDto>(HttpMethod.Get, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);

    private async ValueTask<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));

        if (requestId is not null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);

        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(PayPalSanitizer.Redact(text));

        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidOperationException("PayPal response body was empty.");
    }

    private async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUrl, "/v1/oauth2/token"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
        request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(PayPalSanitizer.Redact(body));

        using var doc = JsonDocument.Parse(body);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("PayPal token response did not include access_token.");
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var expiresElement) ? expiresElement.GetInt32() : 300;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));

        return _accessToken;
    }
}
