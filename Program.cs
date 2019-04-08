using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.IO;

namespace SpeachToText
{
    class Program
    {
        private static readonly HttpClient client = new HttpClient();

        // NOTE!!! Keys are region specific.  Change endpoint url to correct region as needed.  Below it is eastus.
        private static readonly string speechAPIKey = "<Your speech key goes here>";
        private static Guid AdaptedAcousticId = new Guid("<Acoustic model goes here>");
        private static Guid AdaptedLanguageId = new Guid("<Language model goes here>");
        private static string SourceAudioFile = "<URI to audio file goes here>";
        // East US region URL
        private static readonly string SpeechEndPointURL = "https://<YOUR_REGION>.cris.ai/api/speechtotext/v2/transcriptions";

        private static readonly string OneAPIOperationLocationHeaderKey = "Operation-Location";

        static void Main(string[] args)
        {
            client.DefaultRequestHeaders
            .Accept
            .Add(new MediaTypeWithQualityHeaderValue("application/json"));//ACCEPT header

            client.Timeout = TimeSpan.FromMinutes(25);

            DeleteTranscriptionsAsync().Wait();

            ObtainTranscriptionAsync().Wait();

        }

        static async Task ObtainTranscriptionAsync() 
        {
            // Queue transcription
            var transcriptionResponse = await QueueTranscriptionAsync().ConfigureAwait(false);

            // get the transcription Id from the location URI
            var createdTranscriptions = new List<Guid>();
            createdTranscriptions.Add(new Guid(transcriptionResponse.OperationLocation.ToString().Split('/').LastOrDefault()));
            
            var transcriptions = await GetTranscriptionsAsync().ConfigureAwait(false);
             
                        // check for the status of our transcriptions every 30 sec. (can also be 1, 2, 5 min depending on usage)
            int completed = 0, running = 0, notStarted = 0;
            while (completed < 1)
            {
                // get all transcriptions for the user
                transcriptions = await GetTranscriptionsAsync().ConfigureAwait(false);

                completed = 0; running = 0; notStarted = 0;
                // for each transcription in the list we check the status
                foreach (var transcription in transcriptions)
                {
                    switch(transcription.Status)
                    {
                        case "Failed":
                          Console.WriteLine("Transcription failed: " + transcription.Status);
                          Console.WriteLine(transcription.StatusMessage);
                          break;

                        case "Succeeded":
                            // we check to see if it was one of the transcriptions we created from this client.
                            if (!createdTranscriptions.Contains(transcription.Id))
                            {
                                // not creted form here, continue
                                continue;
                            }
                            completed++;
                            
                            // if the transcription was successfull, check the results
                            if (transcription.Status == "Succeeded")
                            {
                                var resultsUri = transcription.ResultsUrls["channel_0"];

                                WebClient webClient = new WebClient();

                                var filename = Path.GetTempFileName();
                                webClient.DownloadFile(resultsUri, filename);

                                var results = File.ReadAllText(filename);
                                Console.WriteLine("Transcription succedded. Results: ");
                                Console.WriteLine(results);
                            }
                            break;

                        case "Running":
                            running++;
                            break;

                        case "NotStarted":
                            notStarted++;
                            break;
                    }
                }

                Console.WriteLine(string.Format("Transcriptions status: {0} completed, {1} running, {2} not started yet", completed, running, notStarted));
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        static async Task DeleteTranscriptionsAsync() 
        {
            var transcriptions = await GetTranscriptionsAsync().ConfigureAwait(false);

            foreach (var item in transcriptions)
            {
                await DeleteTranscriptionAsync(item.Id).ConfigureAwait(false);
                Console.WriteLine("Item.Id " + item.Id + " deleted.");
            }
        }

        static Task DeleteTranscriptionAsync(Guid id)
        {
            var path = $"{SpeechEndPointURL}/{id}";
            return client.DeleteAsync(path);
        }

        static async Task<CognitiveServicesResponse> QueueTranscriptionAsync()
        {
             var modelIds = new[] { AdaptedAcousticId, AdaptedLanguageId };
            var models = modelIds.Select(m => ModelIdentity.Create(m)).ToList();

            var transcriptionDefinition = TranscriptionDefinition.Create("Transcription using a speech sample", 
                                                                         "An optional description of the transcription.",
                                                                           "en-US",new Uri(SourceAudioFile), models);

           string json = JsonConvert.SerializeObject(transcriptionDefinition);
            var content = new StringContent(json, Encoding.UTF8, "application/json");//CONTENT-TYPE header
           
           content.Headers.Add("Ocp-Apim-Subscription-Key", speechAPIKey);
            //client.DefaultRequestHeaders.Add("Content-Type","application/json");
           var response = await client.PostAsync(SpeechEndPointURL, content).ConfigureAwait(false);

           var responseString = await response.Content.ReadAsStringAsync();
            Console.WriteLine( responseString);

            
            foreach (var header in response.Headers)
            {
                Console.WriteLine("CacheControl {0}={1}", header.Key, header.Value.FirstOrDefault());
            };

            IEnumerable<string> headerValues;
            if (response.Headers.TryGetValues(OneAPIOperationLocationHeaderKey, out headerValues))
            {
                if (headerValues.Any())
                {
                    Console.WriteLine( headerValues.First());
                }
            }

            return CreateResponseXLimitInfoObject(response.Headers);

           
        }

        static async Task<IEnumerable<Transcription>> GetTranscriptionsAsync()
        {
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", speechAPIKey);
            var response = await client.GetAsync(SpeechEndPointURL).ConfigureAwait(false);
            var transcriptions = await response.Content.ReadAsAsync<IEnumerable<Transcription>>().ConfigureAwait(false);

            return transcriptions;
        }

        static CognitiveServicesResponse CreateResponseXLimitInfoObject(HttpResponseHeaders headers)
        {
            CognitiveServicesResponse responseLimitInfo = new CognitiveServicesResponse();
            foreach (var header in headers)
            {
                Console.WriteLine("CacheControl {0}={1}", header.Key, header.Value.FirstOrDefault());
                switch (header.Key)
                {
                    case "Operation-Location": responseLimitInfo.OperationLocation = header.Value.FirstOrDefault(); break;
                    case "X-RateLimit-Limit": responseLimitInfo.XRateLimitLimit = int.Parse(header.Value.FirstOrDefault()); break;
                    case "X-RateLimit-Remaining": responseLimitInfo.XRateLimitRemaining = int.Parse(header.Value.FirstOrDefault()); break;
                    case "X-RateLimit-Reset": responseLimitInfo.XRateLimitReset = Convert.ToDateTime(header.Value.FirstOrDefault()); break;

                    default: break;
                }
            };

            return responseLimitInfo;
        }

    }
    
    public sealed class TranscriptionProperties
    {
        private TranscriptionProperties(string profanityFilterMode, string punctuationMode, bool addWordLevelTimestamps)
        {
            this.ProfanityFilterMode = profanityFilterMode;
            this.PunctuationMode = punctuationMode;
            this.AddWordLevelTimestamps = addWordLevelTimestamps;
        }

        public string ProfanityFilterMode { get; set; }
        public string PunctuationMode { get; set; }
        public bool AddWordLevelTimestamps { get; set; }

        public static TranscriptionProperties Create(
           string profanityFilterMode,
           string punctuationMode,
           bool addWordLevelTimestamps)
        {
            return new TranscriptionProperties(profanityFilterMode, punctuationMode, addWordLevelTimestamps);
        }
    }

    public sealed class TranscriptionDefinition
    {
        private TranscriptionDefinition(string name, string description, string locale, Uri recordingsUrl)
        {
            this.Name = name;
            this.Description = description;
            this.RecordingsUrl = recordingsUrl;
            this.Locale = locale;
        }

        private TranscriptionDefinition(string name, string description, string locale, Uri recordingsUrl, IEnumerable<ModelIdentity> models)
        {
            this.Name = name;
            this.Description = description;
            this.RecordingsUrl = recordingsUrl;
            this.Locale = locale;
            this.Models = models;
            this.Properties = TranscriptionProperties.Create("Masked", "DictatedAndAutomatic", true);
        }

        /// <inheritdoc />
        public string Name { get; set; }

        /// <inheritdoc />
        public string Description { get; set; }

        /// <inheritdoc />
        public Uri RecordingsUrl { get; set; }

        public string Locale { get; set; }

        public TranscriptionProperties Properties { get; set; }

        public IEnumerable<ModelIdentity> Models { get; set; }

        public static TranscriptionDefinition Create(
            string name,
            string description,
            string locale,
            Uri recordingsUrl)
        {
            return new TranscriptionDefinition(name, description, locale, recordingsUrl, null);
        }

        public static TranscriptionDefinition Create(
            string name,
            string description,
            string locale,
            Uri recordingsUrl,
            IEnumerable<ModelIdentity> models)
        {
            return new TranscriptionDefinition(name, description, locale, recordingsUrl, models);
        }
    }

    public sealed class ModelIdentity
    {
        private ModelIdentity(Guid id)
        {
            this.Id = id;
        }

        public Guid Id { get; private set; }

        public static ModelIdentity Create(Guid Id)
        {
            return new ModelIdentity(Id);
        }
    }

     public sealed class CognitiveServicesResponse
     {
        public string OperationLocation { get; set; } // Location of cog services processing URL
        public int RetryAfter { get; set; }  // Retry access to transaction after X seconds
        public int XRateLimitLimit { get; set; } // max allowed cog svc transactions in flight
        public int XRateLimitRemaining { get; set; }// max remaining allowed calls before limit it
        public DateTime XRateLimitReset { get; set; }// limit reset at this DateTime
     }

    public sealed class Transcription
    {
        [JsonConstructor]
        private Transcription(Guid id, string name, string description, string locale, DateTime createdDateTime, DateTime lastActionDateTime, string status, Uri recordingsUrl, IReadOnlyDictionary<string, string> resultsUrls)
        {
            this.Id = id;
            this.Name = name;
            this.Description = description;
            this.CreatedDateTime = createdDateTime;
            this.LastActionDateTime = lastActionDateTime;
            this.Status = status;
            this.Locale = locale;
            this.RecordingsUrl = recordingsUrl;
            this.ResultsUrls = resultsUrls;
        }

        /// <inheritdoc />
        public string Name { get; set; }

        /// <inheritdoc />
        public string Description { get; set; }

        /// <inheritdoc />
        public string Locale { get; set; }

        /// <inheritdoc />
        public Uri RecordingsUrl { get; set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> ResultsUrls { get; set; }

        public Guid Id { get; set; }

        /// <inheritdoc />
        public DateTime CreatedDateTime { get; set; }

        /// <inheritdoc />
        public DateTime LastActionDateTime { get; set; }

        /// <inheritdoc />
        public string Status { get; set; }

        public string StatusMessage { get; set; }
    }
}
