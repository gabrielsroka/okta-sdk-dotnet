﻿// <copyright file="DefaultRequestExecutor.cs" company="Okta, Inc">
// Copyright (c) 2014-2017 Okta, Inc. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Okta.Sdk.Configuration;

namespace Okta.Sdk.Internal
{
    /// <summary>
    /// The default implementation of <see cref="IRequestExecutor"/> that uses <c>System.Net.Http</c>.
    /// </summary>
    public sealed class DefaultRequestExecutor : IRequestExecutor
    {
        private const string OktaClientUserAgentName = "oktasdk-dotnet";

        private readonly string _orgUrl;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultRequestExecutor"/> class.
        /// </summary>
        /// <param name="configuration">The client configuration.</param>
        /// <param name="logger">The logging interface.</param>
        public DefaultRequestExecutor(OktaClientConfiguration configuration, ILogger logger)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrEmpty(configuration.Token))
            {
                throw new ArgumentNullException(nameof(configuration.Token));
            }

            _orgUrl = EnsureCorrectOrgUrl(configuration.OrgUrl);
            _logger = logger;

            _httpClient = CreateClient(
                _orgUrl,
                configuration.Token,
                configuration.ConnectionTimeout,
                configuration.Proxy,
                logger);
        }

        private static string EnsureCorrectOrgUrl(string orgUrl)
        {
            if (string.IsNullOrEmpty(orgUrl))
            {
                throw new ArgumentNullException(nameof(orgUrl));
            }

            if (!orgUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Org URL must start with https://");
            }

            if (!orgUrl.EndsWith("/"))
            {
                orgUrl += "/";
            }

            return orgUrl;
        }

        private static HttpClient CreateClient(
            string orgBaseUrl,
            string token,
            int? connectionTimeout,
            ProxyConfiguration proxyConfiguration,
            ILogger logger)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
            };

            if (proxyConfiguration != null)
            {
                handler.Proxy = new DefaultProxy(proxyConfiguration, logger);
            }

            var client = new HttpClient(handler, true)
            {
                BaseAddress = new Uri(orgBaseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(connectionTimeout ?? OktaClientConfiguration.DefaultConnectionTimeout),
            };

            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("SSWS", token);

            // Workaround for https://github.com/dotnet/corefx/issues/11224
            client.DefaultRequestHeaders.Add("Connection", "close");

            return client;
        }

        private string EnsureRelativeUrl(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("The request URI was empty.");
            }

            if (uri.StartsWith(_orgUrl))
            {
                return uri.Replace(_orgUrl, string.Empty);
            }

            if (uri.Contains("://"))
            {
                throw new InvalidOperationException($"The client is configured to connect to {_orgUrl}, but this request URI does not match: ${uri}");
            }

            return uri.TrimStart('/');
        }

        private async Task<HttpResponse<string>> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _logger.LogTrace($"{request.Method} {request.RequestUri}");

            using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogTrace($"{(int)response.StatusCode} {request.RequestUri.PathAndQuery}");

                string stringContent = null;
                if (response.Content != null)
                {
                    stringContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                return new HttpResponse<string>
                {
                    Headers = ExtractHeaders(response),
                    StatusCode = (int)response.StatusCode,
                    Payload = stringContent,
                };
            }
        }

        private static void ApplyHeadersToRequest(HttpRequestMessage request, IEnumerable<KeyValuePair<string, string>> headers)
        {
            if (headers == null || !headers.Any())
            {
                return;
            }

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }
        }

        private static IEnumerable<KeyValuePair<string, IEnumerable<string>>> ExtractHeaders(HttpResponseMessage response)
            => response.Headers.Concat(response.Content.Headers);

        /// <inheritdoc/>
        public Task<HttpResponse<string>> GetAsync(string href, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
        {
            var path = EnsureRelativeUrl(href);

            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            ApplyHeadersToRequest(request, headers);

            return SendAsync(request, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<HttpResponse<string>> PostAsync(string href, IEnumerable<KeyValuePair<string, string>> headers, string body, CancellationToken cancellationToken)
        {
            var path = EnsureRelativeUrl(href);

            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
            ApplyHeadersToRequest(request, headers);

            request.Content = string.IsNullOrEmpty(body)
                ? null
                : new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            return SendAsync(request, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<HttpResponse<string>> PutAsync(string href, IEnumerable<KeyValuePair<string, string>> headers, string body, CancellationToken cancellationToken)
        {
            var path = EnsureRelativeUrl(href);

            var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative));
            ApplyHeadersToRequest(request, headers);

            request.Content = string.IsNullOrEmpty(body)
                ? null
                : new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            return SendAsync(request, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<HttpResponse<string>> DeleteAsync(string href, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
        {
            var path = EnsureRelativeUrl(href);

            var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative));
            ApplyHeadersToRequest(request, headers);

            return SendAsync(request, cancellationToken);
        }
    }
}
