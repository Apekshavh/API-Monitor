using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;

namespace SB.OrginationApi.Monitor
{
    public static class QuoteMonitor
    {
        private static readonly string instrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");

        private static readonly TelemetryConfiguration telemetryConfiguration = new TelemetryConfiguration(instrumentationKey, new InMemoryChannel { EndpointAddress = Environment.GetEnvironmentVariable("SB_QUOTE_URL") });
        private static readonly TelemetryClient telemetryClient = new TelemetryClient(telemetryConfiguration);

        /// <summary>
        /// Timer triggered Function and runs every minute
        /// </summary>
        /// <param name="myTimer"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("Quote-Availability-Monitor")]
        public static async System.Threading.Tasks.Task RunAsync([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"Quote-Availability-Monitor Function Started at: {DateTime.Now}");
            string token = await GetBearerTokenFromKeyVault();

            await Run(token, myTimer, log);
            log.LogInformation($"Quote-Availability-Monitor Function Ended at: {DateTime.Now}");

        }

        /// <summary>
        /// Get Bearer token from the Key vau
        /// </summary>
        /// <returns></returns>
        private static async Task<string> GetBearerTokenFromKeyVault()
        {
            string secretName = Environment.GetEnvironmentVariable("SECRET_NAME");
            string keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
            var keyVaultUrl = "https://" + keyVaultName + ".vault.azure.net/";

            AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
            KeyVaultClient keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            var secret = await keyVaultClient.GetSecretAsync(keyVaultUrl, secretName)
                        .ConfigureAwait(false);
            string token = secret.Value;
            return token;
        }

        /// <summary>
        /// Run the Availability Test
        /// </summary>
        /// <param name="token"></param>
        /// <param name="myTimer"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        public async static Task Run(string token, TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"Entering Test Run at: {DateTime.Now}");

            if (myTimer.IsPastDue)
            {
                log.LogWarning($"[Warning]: Timer is running late! Last ran at: {myTimer.ScheduleStatus.Last}");
            }

            string testName = Environment.GetEnvironmentVariable("TEST_NAME");
            string location = Environment.GetEnvironmentVariable("TEST_REGION_NAME");

            log.LogInformation($"Executing availability test run for {testName} at: {DateTime.Now}");
            string operationId = Guid.NewGuid().ToString("N");

            var availability = new AvailabilityTelemetry
            {
                Id = operationId,
                Name = testName,
                RunLocation = location,
                Success = false
            };

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                string stringBodyContent = await GetRandomBodyContentAsync(log);

                await RunAvailbiltyTestAsync(token, log, stringBodyContent);
                availability.Success = true;
                stopwatch.Stop();
                availability.Duration = stopwatch.Elapsed;
                availability.Timestamp = DateTimeOffset.UtcNow;
                if (availability.Duration.TotalSeconds > Convert.ToInt32(Environment.GetEnvironmentVariable("RESPONSE_TIME_SLA_IN_SEC")))
                {
                    availability.Success = false;
                    throw new Exception("Quote API does not meet the SLA of " + Environment.GetEnvironmentVariable("RESPONSE_TIME_SLA_IN_SEC") + "Seconds");
                }

            }
            catch (Exception ex)
            {
                availability.Message = ex.Message;
                var exceptionTelemetry = new ExceptionTelemetry(ex);
                exceptionTelemetry.Context.Operation.Id = operationId;
                exceptionTelemetry.Properties.Add("TestName", testName);
                exceptionTelemetry.Properties.Add("TestLocation", location);
                telemetryClient.TrackException(exceptionTelemetry);
            }
            finally
            {
                if (stopwatch.IsRunning)
                {
                    stopwatch.Stop();
                    availability.Duration = stopwatch.Elapsed;
                    availability.Timestamp = DateTimeOffset.UtcNow;
                }
                telemetryClient.TrackAvailability(availability);
                telemetryClient.Flush();
            }

        }

        /// <summary>
        /// Random BodyContent
        /// </summary>
        /// <returns></returns>
        private static async Task<string> GetRandomBodyContentAsync(ILogger log)
        {
            //01. Store all the body content in a Storage Table, SerialNumber, BodyContent
            //02. Randomize the serialNumber
            //03. Get the Body for the respective serial Number

            Random random = new Random();
            var serialNumber = random.Next(1, Convert.ToInt32(Environment.GetEnvironmentVariable("MAX_MESSAGE_NUMBERS"))).ToString();
            log.LogInformation($"Selecting Body Content for serial number {serialNumber}");

            //TODO : GET the Body Content from the storage Table
            //TABLE NAME : OrgAPITestBodyContent
            //Get Body for the serial Number
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse("AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;DefaultEndpointsProtocol=http;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;");
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference("OrgAPITestBodyContent");
            TableOperation retrieveOperation = TableOperation.Retrieve<orgAPITestBodyContent>("SB", serialNumber);
            TableResult result = await table.ExecuteAsync(retrieveOperation);
            string bodyContent = ((orgAPITestBodyContent)result.Result).BodyContent;
            log.LogInformation($"{bodyContent}");
            return bodyContent;
        }



        /// <summary>
        /// Run the availability Test
        /// </summary>
        /// <param name="token"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        private static async Task RunAvailbiltyTestAsync(string token, ILogger log, string bodyContent)
        {
            log.LogInformation($"Executing RunAvailbiltyTestAsync");
            await PostQuote(token, log, bodyContent);
        }

        private static async Task<string> PostQuote(string token, ILogger log, string bodyContent)
        {

            string BodyContent = bodyContent; //"{\"loanType\":\"PersonalLoansRedirect\",  \"loanPurpose\":\"Car\",  \"loanAmount\":3000,  \"term\":24,  \"deposit\":0,  \"partnerInfo\":{     \"partnerId\":\"82ae6899-9c41-4295-a592-cb1d9f663b42\",     \"partnerTrackingId\":\"21ac52c3-897f-11e9-b050-nbnbnbnb\",     \"partnerAdditionalReference\":null  },  \"applicants\":[     {        \"title\":\"Ms\",        \"forename\":\"Ann\",        \"surname\":\"Heselden\",        \"gender\":\"Female\",        \"dateOfBirth\":\"01/07/1963\",        \"emailAddress\":\"anna.schechter@shawbrook.co.uk\",        \"residentialStatus\":\"OwnerOccupier\",        \"maritalStatus\":\"Cohabiting\",        \"grossIncome\":30000,        \"incomeFrequency\":\"Annually\",        \"employmentStatus\":\"Employed\",        \"contactInfo\":{           \"telephoneNumber\":\"07789826266\"        }     }  ],  \"addresses\":[     {        \"houseNumber\":75,        \"postcode\":\"BA13 3BN\"     }  ]}";


            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                HttpContent httpContent = new StringContent(BodyContent, Encoding.UTF8);
                httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var response = await client.PostAsync(Environment.GetEnvironmentVariable("SB_QUOTE_URL"), httpContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        await Login.SetToken(log);
                    }
                    await Task.Delay(Convert.ToInt32(Environment.GetEnvironmentVariable("RESPONSE_TIME_SLA_IN_SEC")) * 1000);
                    throw new Exception("Quote Service thrown an Error with status code : " + response.StatusCode.ToString());
                }


            }
            return token;
        }
    }
}

