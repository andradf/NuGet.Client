﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;

namespace NuGet.Protocol
{
    /// <summary>
    /// The <see cref="HttpRetryHandler"/> is for retrying and HTTP request if it times out, has any exception,
    /// or returns a status code of 500 or greater.
    /// </summary>
    public class HttpRetryHandler : IHttpRetryHandler
    {
        /// <summary>
        /// Make an HTTP request while retrying after failed attempts or timeouts.
        /// </summary>
        /// <remarks>
        /// This method accepts a factory to create instances of the <see cref="HttpRequestMessage"/> because
        /// requests cannot always be used. For example, suppose the request is a POST and contains content
        /// of a stream that can only be consumed once.
        /// </remarks>
        public async Task<HttpResponseMessage> SendAsync(
            HttpRetryHandlerRequest request,
            ILogger log,
            CancellationToken cancellationToken)
        {
            var tries = 0;
            HttpResponseMessage response = null;
            var success = false;
            var ownedSemaphore = false;

            while (tries < request.MaxTries && !success)
            {
                if (tries > 0)
                {
                    await Task.Delay(request.RetryDelay, cancellationToken);
                }

                tries++;
                success = true;

                using (var requestMessage = request.RequestFactory())
                {
                    var stopwatch = Stopwatch.StartNew();
                    string requestUri = requestMessage.RequestUri.ToString();
                    
                    try
                    {
                        // The only time that we will be disposing this existing response is if we have 
                        // successfully fetched an HTTP response but the response has an status code indicating
                        // failure (i.e. HTTP status code >= 500).
                        // 
                        // If we don't even get an HTTP response message because an exception is thrown, then there
                        // is no response instance to dispose. Additionally, we cannot use a finally here because
                        // the caller needs the response instance returned in a non-disposed state.
                        //
                        // Also, remember that if an HTTP server continuously returns a failure status code (like
                        // 500 Internal Server Error), we will retry some number of times but eventually return the
                        // response as-is, expecting the caller to check the status code as well. This results in the
                        // success variable being set to false but the response being returned to the caller without
                        // disposing it.
                        response?.Dispose();

                        // Acquire the HTTP semaphore.
                        if (request.Semaphore != null)
                        {
                            await request.Semaphore.WaitAsync();
                            ownedSemaphore = true;
                        }

                        log.LogInformation("  " + string.Format(
                            CultureInfo.InvariantCulture,
                            Strings.Http_RequestLog,
                            requestMessage.Method,
                            requestUri));

                        // Issue the request.
                        var timeoutMessage = string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.Http_Timeout,
                            requestMessage.Method,
                            requestUri,
                            (int)request.RequestTimeout.TotalMilliseconds);
                        response = await TimeoutUtility.StartWithTimeout(
                            timeoutToken => request.HttpClient.SendAsync(requestMessage, request.CompletionOption, timeoutToken),
                            request.RequestTimeout,
                            timeoutMessage,
                            cancellationToken);

                        // Wrap the response stream so that the download can timeout and the HTTP
                        // semaphore is respected.
                        Stream networkStream;
                        if (response.Content != null)
                        {
                            networkStream = await response.Content.ReadAsStreamAsync();
                        }
                        else
                        {
                            networkStream = Stream.Null;
                        }

                        var newContent = new DownloadTimeoutStreamContent(
                            requestUri,
                            networkStream,
                            request.DownloadTimeout,
                            request.Semaphore);
                        ownedSemaphore = false;
                        response.Content = newContent;

                        log.LogInformation("  " + string.Format(
                            CultureInfo.InvariantCulture,
                            Strings.Http_ResponseLog,
                            response.StatusCode,
                            requestUri,
                            stopwatch.ElapsedMilliseconds));

                        if ((int)response.StatusCode >= 500)
                        {
                            success = false;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        DisposeResponse(request.Semaphore, response, ownedSemaphore);

                        throw;
                    }
                    catch (Exception e)
                    {
                        success = false;

                        DisposeResponse(request.Semaphore, response, ownedSemaphore);

                        if (tries >= request.MaxTries)
                        {
                            throw;
                        }

                        log.LogInformation(string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.Log_RetryingHttp,
                            requestMessage.Method,
                            requestUri,
                            requestMessage)
                            + Environment.NewLine
                            + ExceptionUtilities.DisplayMessage(e));
                    }
                }
            }

            return response;
        }

        private static void DisposeResponse(SemaphoreSlim semaphore, HttpResponseMessage response, bool ownedSemaphore)
        {
            response?.Dispose();

            if (ownedSemaphore && semaphore != null)
            {
                semaphore.Release();
            }
        }
    }
}
