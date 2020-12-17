using DistanceMicroservices.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Polly;
using RestSharp;
using MoreLinq;
using System.Threading.Tasks;

namespace DistanceMicroservices.Services
{
    public class GoogleServices
    {
        public ILogger _logger { get; set; }

        public GoogleServices(ILogger log = null)
        {
            _logger = log;
        }


        public async Task<List<DistributionCenterDistance>> GetDistanceDataFromGoogle(string destinationZip, List<GoogleOriginData> branches)
        {
            var distances = new List<DistributionCenterDistance>();
            var distanceTasks = new List<Task<List<DistributionCenterDistance>>>();

            // Send in batches of 100 as to not exceed the request's character limit
            var batchedBranches = branches.Batch(100);

            foreach (var branchesBatch in batchedBranches)
            {
                distanceTasks.Add(GetBatchedDistanceDataFromGoogle(destinationZip, branchesBatch.ToList()));
            }

            var distancesToAdd = await Task.WhenAll(distanceTasks);

            foreach (var dist in distancesToAdd)
            {
                distances.AddRange(dist);
            }

            return distances;
        }


        private async Task<List<DistributionCenterDistance>> GetBatchedDistanceDataFromGoogle(string destinationZip, List<GoogleOriginData> branches)
        {
            var origins = new List<string>();
            var distances = new List<DistributionCenterDistance>();

            foreach (var branch in branches)
            {
                if (branch.Latitude == null || branch.Longitude == null)
                    origins.Add($"{branch.City}+{branch.State}+{branch.Zip}");
                else
                    origins.Add($"{branch.Latitude},{branch.Longitude}");
            }

            var response = await SendDistanceMatrixRequestToGoogle(origins, destinationZip);

            var responseOrigins = response.OriginAddresses;

            for (var i = 0; i < responseOrigins.Length; i++)
            {
                var results = response.Rows[i].Elements;

                for (var j = 0; j < results.Length; j++)
                {
                    var element = results[j];

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


        public async Task<GoogleDistanceMatrixAPIResponse> SendDistanceMatrixRequestToGoogle(List<string> origins, string destination)
        {
            var retryPolicy = Policy.Handle<Exception>().Retry(5, (ex,count) =>
            {
                var errMessage = "Error in SendDistanceMatrixRequestToGoogle";
                _logger.LogWarning("{0}. Retrying... : {1}", errMessage, ex);
                if (count == 5)
                {
#if !DEBUG
                    var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
#endif
                    _logger.LogError(@"{0}: {1}", errMessage, ex);
                }
            });

            return await retryPolicy.Execute(async () =>
             {
                 var originString = string.Join("|", origins);
                 var key = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
                 var baseUrl = $"https://maps.googleapis.com/maps/api/distancematrix/json";

                 var client = new RestClient(baseUrl);
                 var request = new RestRequest(Method.GET)
                     .AddParameter("region", "us", ParameterType.QueryStringWithoutEncode)
                     .AddParameter("key", key, ParameterType.QueryStringWithoutEncode)
                     .AddParameter("origins", originString, ParameterType.QueryStringWithoutEncode)
                     .AddParameter("destinations", destination, ParameterType.QueryStringWithoutEncode);

                 var restResponse = await client.ExecuteAsync(request);
                 var jsonResponse = restResponse.Content;

                 var response = GoogleDistanceMatrixAPIResponse.FromJson(jsonResponse);

                 return response;
             });
        }
    }
}
