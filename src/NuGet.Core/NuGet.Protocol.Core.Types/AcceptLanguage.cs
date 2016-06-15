// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NuGet.Protocol.Core.Types
{
    public static class AcceptLanguage
    {
        public const string HeaderName = "Accept-Language";

        static AcceptLanguage()
        {
            // Set default Accept-Language header string
            AcceptLanguageString = CultureInfo.CurrentUICulture.ToString();
        }

        public static string AcceptLanguageString { get; private set; }

        /// <summary>
        /// Set accept-language header on HttpClient to the static string.
        /// </summary>
        /// <param name="client">Http client</param>
        public static void SetAcceptLanguageHeader(HttpClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (!string.IsNullOrEmpty(AcceptLanguageString))
            {
                client.DefaultRequestHeaders.AcceptLanguage.Add(
                    StringWithQualityHeaderValue.Parse(AcceptLanguageString));
            }
        }
    }
}