using DistanceMicroservices.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Polly;
using RestSharp;

namespace DistanceMicroservices.Services
{
    public class GoogleServices
    {
        public ILogger _logger { get; set; }

        public GoogleServices(ILogger log = null)
        {
            _logger = log;
        }


        public List<DistributionCenterDistance> GetDistanceDataFromGoogle(string destination, List<GoogleOriginData> branches)
        {
            var distances = new List<DistributionCenterDistance>();

            const int branchesBatchSize = 100;

            for (var i = 0; i < branches.Count; i += branchesBatchSize)
            {
                var branchesBatch = branches.Skip(i).Take(branchesBatchSize).ToList();
                
                var distancesToAdd = GetBatchedDistanceDataFromGoogle(destination, branchesBatch);

                distances.AddRange(distancesToAdd);
            }

            return distances;
        }


        private List<DistributionCenterDistance> GetBatchedDistanceDataFromGoogle(string destination, List<GoogleOriginData> branches)
        {
            var origins = new List<string>();
            var distances = new List<DistributionCenterDistance>();

            foreach (var branch in branches)
            {
                if (branch.Latitude == null && branch.Longitude == null)
                {
                    // Remove special characters from address, otherwise Google will throw exception
                    branch.Address1 = Regex.Replace(branch.Address1, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);

                    origins.Add($"{branch.Address1}%2B{branch.City}%2B{branch.State}%2B{branch.Zip}");
                }
                else
                {
                    origins.Add($"{branch.Latitude}%2C{branch.Longitude}");
                }
            }

            var response = SendDistanceMatrixRequestToGoogle(origins, destination);

            var responseOrigins = response.OriginAddresses;

            for (var i = 0; i < responseOrigins.Length; i++)
            {
                var results = response.Rows[i].Elements;

                for (var j = 0; j < results.Length; j++)
                {
                    var element = results[j];
                    var destinationZip = destination;

                    if (!element.Status.Equals(Status.Ok)) continue;

                    var distance = element.Distance.Value;
                    var distributionCenter = branches[i].BranchNumber;

                    var distributionCenterDistance = new DistributionCenterDistance()
                    {
                        BranchNumber = distributionCenter,
                        ZipCode = destinationZip,
                        DistanceInMeters = (int)distance
                    };
                    distances.Add(distributionCenterDistance);
                }
            }

            return distances;
        }


        public GoogleDistanceMatrixAPIResponse SendDistanceMatrixRequestToGoogle(List<string> origins, string destination)
        {
            var retryPolicy = Policy.Handle<Exception>().Retry(5, (ex,count) =>
            {
                const string errorMessage = "Error in SendDistanceMatrixRequestToGoogle";
                _logger.LogWarning(ex, $"{errorMessage} . Retrying...");
                if (count == 5)
                {
                    var teamsMessage = new TeamsMessage(errorMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
                    _logger.LogError(ex, errorMessage);
                }
            });

            return retryPolicy.Execute(() =>
            {
                var originString = string.Join("%7C", origins);
                var key = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
                var baseUrl = $"https://maps.googleapis.com/maps/api/distancematrix/json";

                var client = new RestClient(baseUrl);
                var request = new RestRequest(Method.GET)
                    .AddParameter("region", "us")
                    .AddParameter("key", key)
                    .AddParameter("origins", originString)
                    .AddParameter("destinations", destination);

                var jsonResponse = client.Execute(request).Content;

                var response = GoogleDistanceMatrixAPIResponse.FromJson(jsonResponse);

                return response;
            });
        }
    }
}
