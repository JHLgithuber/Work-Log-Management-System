using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkLogManagementSystem_UI.Models;

namespace WorkLogManagementSystem_UI.Services;

internal sealed class RefreshingAuthHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private readonly Uri _baseAddress;
    private readonly Func<(string AccessToken, string RefreshToken)> _tokenProvider;
    private readonly Func<TokenResponse, Task> _tokenUpdated;

    public RefreshingAuthHandler(
        Uri baseAddress,
        Func<(string AccessToken, string RefreshToken)> tokenProvider,
        Func<TokenResponse, Task> tokenUpdated)
        : base(new HttpClientHandler())
    {
        _baseAddress = baseAddress;
        _tokenProvider = tokenProvider;
        _tokenUpdated = tokenUpdated;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        BufferedRequest buffered = await BufferedRequest.CreateAsync(request, cancellationToken);
        string attemptedAccessToken = _tokenProvider().AccessToken;
        using HttpRequestMessage firstRequest = buffered.Create(attemptedAccessToken);
        HttpResponseMessage response = await base.SendAsync(firstRequest, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrWhiteSpace(_tokenProvider().RefreshToken))
        {
            return response;
        }

        response.Dispose();
        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            (string currentAccessToken, string currentRefreshToken) = _tokenProvider();
            if (currentAccessToken == attemptedAccessToken)
            {
                TokenResponse tokens = await RefreshAsync(currentRefreshToken, cancellationToken);
                await _tokenUpdated(tokens);
                currentAccessToken = tokens.AccessToken;
            }

            using HttpRequestMessage retryRequest = buffered.Create(currentAccessToken);
            return await base.SendAsync(retryRequest, cancellationToken);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { BaseAddress = _baseAddress };
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshTokenRequest { RefreshToken = refreshToken },
            AppJsonSerializerContext.TypeInfo<RefreshTokenRequest>(),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(
                   AppJsonSerializerContext.TypeInfo<TokenResponse>(),
                   cancellationToken)
               ?? throw new InvalidOperationException("The backend returned an empty refresh response.");
    }

    private sealed record BufferedRequest(HttpMethod Method, Uri? RequestUri, byte[]? Content, string? ContentType)
    {
        public static async Task<BufferedRequest> CreateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[]? content = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return new BufferedRequest(
                request.Method,
                request.RequestUri,
                content,
                request.Content?.Headers.ContentType?.ToString());
        }

        public HttpRequestMessage Create(string accessToken)
        {
            HttpRequestMessage request = new(Method, RequestUri);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            if (Content is not null)
            {
                request.Content = new ByteArrayContent(Content);
                if (!string.IsNullOrWhiteSpace(ContentType))
                {
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
                }
            }

            return request;
        }
    }
}
