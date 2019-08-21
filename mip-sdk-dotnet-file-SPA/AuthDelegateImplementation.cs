/*
*
* Copyright (c) Microsoft Corporation.
* All rights reserved.
*
* This code is licensed under the MIT License.
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files(the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions :
*
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*
*/

using System;
using System.Configuration;
using Microsoft.InformationProtection;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
using System.Windows.Forms;

namespace MipSdkDotNetQuickstart
{
    public class AuthDelegateImplementation : IAuthDelegate
    {
        // Set the redirect URI from the AAD Application Registration.
        private static readonly string redirectUri = ConfigurationManager.AppSettings["ida:RedirectUri"];

        // Fetch cert thumbprint, appkey, and DoCertAuth flag from app.config
        private static readonly string certThumb = ConfigurationManager.AppSettings["ida:CertThumbprint"];
        private static readonly bool doCertAuth = Convert.ToBoolean(ConfigurationManager.AppSettings["ida:DoCertAuth"]);
        private static readonly string clientSecret = ConfigurationManager.AppSettings["ida:ClientSecret"];


        // Fetch tenant name from app.config
        // We use the tenant ID to append to the authority, as we need this as a hint to find the correct tenant.
        // Using the common endpoint without tenant throws an exception. 
        private static readonly string tenant = ConfigurationManager.AppSettings["ida:Tenant"];

        private ApplicationInfo appInfo;        
        private TokenCache tokenCache = new TokenCache();
        
        public AuthDelegateImplementation(ApplicationInfo appInfo)
        {
            this.appInfo = appInfo;
        }

        /// <summary>
        /// AcquireToken is called by the SDK when auth is required for an operation. 
        /// Adding or loading an IFileEngine is typically where this will occur first.
        /// The SDK provides all three parameters below.Identity from the EngineSettings.
        /// Authority and resource are provided from the 401 challenge.
        /// The SDK cares only that an OAuth2 token is returned.How it's fetched isn't important.
        /// In this sample, we fetch the token using Active Directory Authentication Library(ADAL).
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="authority"></param>
        /// <param name="resource"></param>
        /// <returns>The OAuth2 token for the user</returns>
        public string AcquireToken(Identity identity, string authority, string resource, string claim)
        {
            // Append tenant to authority.
            authority = string.Format("{0}/{1}", authority, tenant);

            AuthenticationContext authContext = new AuthenticationContext(authority, tokenCache);
            AuthenticationResult result;

            if (doCertAuth)
            {
                Console.WriteLine("Performing certificate based auth with {0}", certThumb);

                // Read cert from local machine
                var cert = ReadCertificateFromStore(certThumb);
                // Use cert to build ClientAssertionCertificate
                var certcred = new ClientAssertionCertificate(appInfo.ApplicationId, cert);
                result = authContext.AcquireTokenAsync(resource, certcred).Result;
            }

            else
            {
                Console.WriteLine("Performing client secret based auth.");
                var clientCred = new ClientCredential(appInfo.ApplicationId, clientSecret);
                result = authContext.AcquireTokenAsync(resource, clientCred).Result;
            }            
            // Return the token. The token is sent to the resource.
            return result.AccessToken;
        }

        private static X509Certificate2 ReadCertificateFromStore(string thumbprint)
        {
            X509Certificate2 cert = null;
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            X509Certificate2Collection certCollection = store.Certificates;

            // Find unexpired certificates.
            X509Certificate2Collection currentCerts = certCollection.Find(X509FindType.FindByTimeValid, DateTime.Now, false);

            // From the collection of unexpired certificates, find the ones with the correct name.
            X509Certificate2Collection signingCert = currentCerts.Find(X509FindType.FindByThumbprint, thumbprint, false);

            // Return the first certificate in the collection, has the right name and is current.
            cert = signingCert.OfType<X509Certificate2>().OrderByDescending(c => c.NotBefore).FirstOrDefault();
            store.Close();
            return cert;
        }



    }
}
