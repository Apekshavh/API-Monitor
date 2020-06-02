using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;

namespace SB.OrginationApi.Monitor
{
    public static class Login
    {
        private static readonly string UserName = Environment.GetEnvironmentVariable("ORG_USER_NAME");// = "testretailer_test_pl_api";
        private static readonly string Password = Environment.GetEnvironmentVariable("ORG_PWD"); // = "GGD7FxW2*EJ!M8ta";

        /// <summary>
        /// Function trigger every 3599 Seconds or lesser or configured interval to get and set the token in the Secret 
        /// </summary>
        /// <param name="myTimer"></param>
        /// <param name="log"></param>
        /// <returns></returns>

        [FunctionName("Login-SetToken")]
        public static async Task RunAsync([TimerTrigger("0 */45 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            await SetToken(log);
        }

        /// <summary>
        /// Set the token to the Azure Secret keyvault
        /// </summary>
        /// <param name="log"></param>
        /// <returns></returns>
        public static async Task SetToken(ILogger log)
        {
            try
            {
                var accessToken = await GetAccessToken();
                await SetTheAccessTokenToKeyVault(accessToken);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Token not set");
                throw ex;
            }
        }

        /// <summary>
        /// Set the Bearer token to the Key Vault
        /// </summary>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        private static async Task SetTheAccessTokenToKeyVault(string accessToken)
        {
            string secretName = Environment.GetEnvironmentVariable("SECRET_NAME");
            string keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
            var keyVaultUrl = "https://" + keyVaultName + ".vault.azure.net/";

            AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
            KeyVaultClient keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            var secret = keyVaultClient.SetSecretAsync(keyVaultUrl, secretName, accessToken).Result;

            SecretAttributes attributes = new SecretAttributes
            {
                Expires = DateTime.UtcNow.AddMinutes(45)
            };
            secret = await keyVaultClient.UpdateSecretAsync(keyVaultUrl + "secrets/" + secretName, null, attributes, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the bearer token from login URL
        /// </summary>
        /// <returns></returns>
        private static async Task<string> GetAccessToken()
        {
            var formVariables = new Dictionary<string, string>();
            formVariables.Add("grant_type", "password");
            formVariables.Add("username", UserName);
            formVariables.Add("password", Password);
            var content = new FormUrlEncodedContent(formVariables);
            string token = string.Empty;

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

                var response = await client.PostAsync(Environment.GetEnvironmentVariable("SB_LOGIN_URL"), content);
                var responseBody = await response.Content.ReadAsStringAsync();
                JObject jObject = JObject.Parse(responseBody);
                token = jObject["access_token"].ToString();

            }
            return token;
        }
    }
}
